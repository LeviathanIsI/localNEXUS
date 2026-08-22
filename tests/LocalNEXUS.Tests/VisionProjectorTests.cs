using System.IO;
using System.Text;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Finding the multimodal projector a local vision model needs, and launching with it.
/// </summary>
/// <remarks>
/// The whole point of this feature is that nobody has to know a projector exists. What has to hold
/// is that the file is found by what is inside it rather than by what it is called, that a model
/// with no projector beside it is refused when it is chosen rather than at the first image, and
/// that the argument actually reaches the command line.
///
/// The GGUF files here are written byte by byte, header only and no tensors, which is enough
/// because only the header is ever read. Nothing is downloaded and no server is started.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class VisionProjectorTests
{
    /// <summary>Writes the header of a GGUF file, which is all the reader looks at.</summary>
    private sealed class GgufBuilder
    {
        private readonly List<Action<BinaryWriter>> _pairs = new();

        public uint Version { get; set; } = 3;

        public GgufBuilder String(string key, string value)
        {
            _pairs.Add(w =>
            {
                Key(w, key);
                w.Write(8u);
                var bytes = Encoding.UTF8.GetBytes(value);
                w.Write((ulong)bytes.Length);
                w.Write(bytes);
            });

            return this;
        }

        public GgufBuilder Bool(string key, bool value)
        {
            _pairs.Add(w =>
            {
                Key(w, key);
                w.Write(7u);
                w.Write((byte)(value ? 1 : 0));
            });

            return this;
        }

        public GgufBuilder Int32(string key, int value)
        {
            _pairs.Add(w =>
            {
                Key(w, key);
                w.Write(5u);
                w.Write(value);
            });

            return this;
        }

        /// <summary>A vocabulary sized array of strings, which is what a real model's header is mostly made of.</summary>
        public GgufBuilder StringArray(string key, int count)
        {
            _pairs.Add(w =>
            {
                Key(w, key);
                w.Write(9u);
                w.Write(8u);
                w.Write((ulong)count);

                for (var i = 0; i < count; i++)
                {
                    var bytes = Encoding.UTF8.GetBytes($"token{i}");
                    w.Write((ulong)bytes.Length);
                    w.Write(bytes);
                }
            });

            return this;
        }

        public GgufBuilder Int32Array(string key, int count)
        {
            _pairs.Add(w =>
            {
                Key(w, key);
                w.Write(9u);
                w.Write(5u);
                w.Write((ulong)count);

                for (var i = 0; i < count; i++)
                {
                    w.Write(i);
                }
            });

            return this;
        }

        public string WriteTo(string path)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(Encoding.ASCII.GetBytes("GGUF"));
            writer.Write(Version);
            writer.Write(0UL);
            writer.Write((ulong)_pairs.Count);

            foreach (var pair in _pairs)
            {
                pair(writer);
            }

            return path;
        }

        private static void Key(BinaryWriter writer, string key)
        {
            var bytes = Encoding.UTF8.GetBytes(key);
            writer.Write((ulong)bytes.Length);
            writer.Write(bytes);
        }
    }

    private static GgufBuilder Projector() => new GgufBuilder()
        .String("general.architecture", "clip")
        .Bool("clip.has_vision_encoder", true)
        .Bool("clip.has_audio_encoder", false)
        .String("clip.projector_type", "qwen2vl_merger")
        .Int32("clip.vision.image_size", 448);

    private static GgufBuilder Model() => new GgufBuilder()
        .String("general.architecture", "qwen2vl")
        .Int32("qwen2vl.block_count", 36)
        .StringArray("tokenizer.ggml.tokens", 2_000)
        .Int32Array("tokenizer.ggml.token_type", 2_000)
        .String("tokenizer.chat_template", "{% for message in messages %}");

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "localnexus-projector", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// A projector says what it is in its own header, which is what makes this findable.
    /// </summary>
    /// <remarks>
    /// The key is the one llama.cpp's loader reads. Nothing but a projector carries it, which is
    /// why the answer does not have to depend on a file name.
    /// </remarks>
    [Fact]
    public void AProjectorDeclaresAVisionEncoder()
    {
        var path = Projector().WriteTo(Path.Combine(Folder(), "anything-at-all.gguf"));

        var header = GgufMetadata.Read(path);

        Assert.NotNull(header);
        Assert.True(header!.HasVisionEncoder);
        Assert.Equal("clip", header.Architecture);
        Assert.Equal("qwen2vl_merger", header.ProjectorType);
    }

    /// <summary>An ordinary model does not, however it is named.</summary>
    /// <remarks>
    /// The tokenizer arrays are the part that matters here. Getting past them means the reader
    /// steps over array values correctly rather than losing its place and reading rubbish.
    /// </remarks>
    [Fact]
    public void AModelDoesNotDeclareAVisionEncoder()
    {
        var path = Model().WriteTo(Path.Combine(Folder(), "mmproj-not-really.gguf"));

        var header = GgufMetadata.Read(path);

        Assert.NotNull(header);
        Assert.False(header!.HasVisionEncoder);
        Assert.Equal("qwen2vl", header.Architecture);
    }

    /// <summary>Something that is not a GGUF at all is a null answer rather than a throw.</summary>
    [Fact]
    public void SomethingElseEntirelyIsNotAGguf()
    {
        var path = Path.Combine(Folder(), "notes.txt");
        File.WriteAllText(path, "this is not a model");

        Assert.Null(GgufMetadata.Read(path));
        Assert.Null(GgufMetadata.Read(Path.Combine(Folder(), "missing.gguf")));
    }

    /// <summary>The first container version wrote its lengths differently, so it is refused.</summary>
    [Fact]
    public void TheOldestContainerIsRefusedRatherThanMisread()
    {
        var builder = Projector();
        builder.Version = 1;

        Assert.Null(GgufMetadata.Read(builder.WriteTo(Path.Combine(Folder(), "ancient.gguf"))));
    }

    /// <summary>The ordinary layout: weights and an mmproj file in one folder.</summary>
    [Fact]
    public void TheProjectorIsFoundBesideTheWeights()
    {
        var folder = Folder();
        var model = Model().WriteTo(Path.Combine(folder, "Qwen2-VL-7B-Q4_K_M.gguf"));
        var projector = Projector().WriteTo(Path.Combine(folder, "mmproj-Qwen2-VL-7B-f16.gguf"));

        var lookup = VisionProjectorLocator.Locate(model);

        Assert.Equal(ProjectorState.Found, lookup.State);
        Assert.True(lookup.IsUsable);
        Assert.Equal(projector, lookup.Path);
        Assert.Contains("qwen2vl_merger", lookup.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A projector named nothing like one is still found, because the name is not the evidence.
    /// </summary>
    /// <remarks>
    /// The name decides what is read first and never what the answer is. Somebody who renamed
    /// their files gets a working vision model rather than a refusal they cannot explain.
    /// </remarks>
    [Fact]
    public void ANameThatSaysNothingIsStillFound()
    {
        var folder = Folder();
        var model = Model().WriteTo(Path.Combine(folder, "weights.gguf"));
        var projector = Projector().WriteTo(Path.Combine(folder, "part-two.gguf"));

        Assert.Equal(projector, VisionProjectorLocator.Locate(model).Path);
    }

    /// <summary>
    /// A file called mmproj that is not one is not accepted, which is the other half of the rule.
    /// </summary>
    [Fact]
    public void ANameThatLiesIsNotAccepted()
    {
        var folder = Folder();
        var model = Model().WriteTo(Path.Combine(folder, "weights.gguf"));
        Model().WriteTo(Path.Combine(folder, "mmproj-looks-right.gguf"));

        var lookup = VisionProjectorLocator.Locate(model);

        Assert.Equal(ProjectorState.NotFound, lookup.State);
        Assert.Null(lookup.Path);
    }

    /// <summary>
    /// No projector is a refusal at selection, and the refusal says what to do about it.
    /// </summary>
    /// <remarks>
    /// This is the failure the whole feature exists to move earlier. Without it the model loads,
    /// looks healthy, and answers 400 to the first image somebody pastes hours later.
    /// </remarks>
    [Fact]
    public void AModelWithNoProjectorIsRefusedWithAReason()
    {
        var folder = Folder();
        var model = Model().WriteTo(Path.Combine(folder, "Qwen2-VL-7B-Q4_K_M.gguf"));

        var lookup = VisionProjectorLocator.Locate(model);

        Assert.Equal(ProjectorState.NotFound, lookup.State);
        Assert.False(lookup.IsUsable);

        Assert.Contains("mmproj", lookup.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(folder, lookup.Message, StringComparison.Ordinal);
    }

    /// <summary>Choosing the projector itself is a mistake worth naming.</summary>
    [Fact]
    public void ChoosingTheProjectorItselfIsSaidPlainly()
    {
        var folder = Folder();
        Model().WriteTo(Path.Combine(folder, "weights.gguf"));
        var projector = Projector().WriteTo(Path.Combine(folder, "mmproj-f16.gguf"));

        var lookup = VisionProjectorLocator.Locate(projector);

        Assert.Equal(ProjectorState.ModelIsAProjector, lookup.State);
        Assert.Contains("the projector itself", lookup.Message, StringComparison.Ordinal);
    }

    /// <summary>A safetensors folder has no projector to find, and says why.</summary>
    [Fact]
    public void SomethingThatIsNotAGgufIsRefused()
    {
        var folder = Folder();
        File.WriteAllText(Path.Combine(folder, "config.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "model.safetensors"), "x");

        var lookup = VisionProjectorLocator.Locate(folder);

        Assert.Equal(ProjectorState.NotAGguf, lookup.State);
        Assert.Contains("GGUF", lookup.Message, StringComparison.Ordinal);
    }

    /// <summary>Nothing chosen is a state, not a crash.</summary>
    [Fact]
    public void NothingChosenIsAnAnswer()
    {
        Assert.Equal(ProjectorState.NotAGguf, VisionProjectorLocator.Locate(null).State);
        Assert.Equal(ProjectorState.NotAGguf, VisionProjectorLocator.Locate("   ").State);
    }

    /// <summary>
    /// The projector reaches the command line as the argument llama-server takes.
    /// </summary>
    /// <remarks>
    /// Checked against the vendored build's own help, which spells it <c>-mm, --mmproj FILE</c>.
    /// </remarks>
    [Fact]
    public void TheProjectorIsPassedAsAnArgument()
    {
        var arguments = new LlamaLaunchOptions { ProjectorPath = @"C:\models\mmproj-f16.gguf" }
            .BuildArguments(@"C:\models\weights.gguf", 8081);

        var index = arguments.ToList().IndexOf("--mmproj");

        Assert.True(index >= 0);
        Assert.Equal(@"C:\models\mmproj-f16.gguf", arguments[index + 1]);

        // And the rest of the launch is what it always was.
        Assert.Contains("-m", arguments);
        Assert.Contains(@"C:\models\weights.gguf", arguments);
        Assert.Contains("8081", arguments);
    }

    /// <summary>An ordinary model is launched exactly as it was before any of this.</summary>
    [Fact]
    public void AModelWithNoProjectorIsLaunchedUnchanged()
    {
        var arguments = new LlamaLaunchOptions().BuildArguments(@"C:\models\coder.gguf", 8080);

        Assert.DoesNotContain("--mmproj", arguments);
        Assert.Equal(10, arguments.Count);
    }

    /// <summary>
    /// The same weights with and without a projector are two servers, not one.
    /// </summary>
    /// <remarks>
    /// One of them can see and one answers 400 to every image, so reusing the wrong one would be
    /// the exact failure this feature removes, arriving by a different route.
    /// </remarks>
    [Fact]
    public void AProjectorMakesItADifferentServer()
    {
        var plain = new LlamaLaunchOptions().BuildServerKey(@"C:\models\weights.gguf");
        var seeing = new LlamaLaunchOptions { ProjectorPath = @"C:\models\mmproj.gguf" }
            .BuildServerKey(@"C:\models\weights.gguf");

        Assert.NotEqual(plain, seeing);

        // And an unchanged launch keeps the key it always had, so nothing else is disturbed.
        Assert.Equal(@"C:\models\weights.gguf|c8192|ngl999", plain);
    }
}
