using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Credentials;

/// <summary>
/// Keys on disk, encrypted for the current Windows user.
/// </summary>
/// <remarks>
/// DPAPI with <see cref="DataProtectionScope.CurrentUser"/>, so the ciphertext is bound to the
/// signed in account and is useless on another machine or to another user on this one. No key
/// management of our own, because inventing key management is how applications end up with a
/// master password stored beside the thing it protects.
///
/// What this is not: protection from something already running as this user. Anything with the
/// user's token can call DPAPI too. It defends against the realistic case, which is a file
/// getting copied, synced, backed up or committed, and it does not pretend to defend against
/// the other one.
///
/// Written whole on every change rather than appended, because the file is a handful of short
/// strings and a partial write of a credential file is a worse outcome than a rewrite.
/// </remarks>
public sealed class DpapiCredentialStore : ICredentialStore
{
    /// <summary>
    /// Mixed into the protected blob. Not a secret and not a password: DPAPI derives from the
    /// user's credentials, and this only ensures a blob lifted from here cannot be handed to
    /// another part of the system that also uses DPAPI and be unprotected there.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LocalNEXUS.credentials.v1");

    private readonly IActivityFeed _feed;
    private readonly Dictionary<string, string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public DpapiCredentialStore(IActivityFeed feed)
    {
        _feed = feed;
        Load();
    }

    /// <summary>Where the encrypted file lives.</summary>
    public static string FilePath => Path.Combine(AppPaths.Root, "credentials.dat");

    /// <inheritdoc />
    public string? Get(string providerId)
        => _keys.TryGetValue(providerId, out var key) && key.Length > 0 ? key : null;

    /// <inheritdoc />
    public bool Has(string providerId) => Get(providerId) is not null;

    /// <inheritdoc />
    public void Set(string providerId, string? key)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            Remove(providerId);
            return;
        }

        _keys[providerId] = key.Trim();
        Save();
    }

    /// <inheritdoc />
    public void Remove(string providerId)
    {
        if (_keys.Remove(providerId))
        {
            Save();
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> ConfiguredProviders() => _keys.Keys.ToList();

    private void Load()
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);

            if (JsonNode.Parse(Encoding.UTF8.GetString(plain)) is not JsonObject root)
            {
                return;
            }

            foreach (var pair in root)
            {
                if (pair.Value?.GetValue<string>() is { Length: > 0 } key)
                {
                    _keys[pair.Key] = key;
                }
            }
        }
        catch (CryptographicException)
        {
            // Written by a different user or on a different machine, which is DPAPI working
            // rather than failing. Say so plainly instead of leaving somebody wondering why
            // their keys vanished, and never delete the file on their behalf.
            _feed.Error(
                "Saved keys could not be read",
                $"{FilePath} was encrypted for a different Windows account or machine, so it cannot be " +
                "decrypted here. Enter the keys again to replace it.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _feed.Error("Saved keys could not be read", $"{FilePath} could not be opened: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureCreated();

            var root = new JsonObject();

            foreach (var pair in _keys)
            {
                root[pair.Key] = pair.Value;
            }

            var plain = Encoding.UTF8.GetBytes(root.ToJsonString());
            var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(FilePath, protectedBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _feed.Error(
                "Keys were not saved",
                $"{FilePath} could not be written: {ex.Message} They will work for this session and be gone " +
                "when the application closes.");
        }
    }
}
