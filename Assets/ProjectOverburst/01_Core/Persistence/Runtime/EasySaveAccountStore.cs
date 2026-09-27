using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Overburst.Persistence
{
    [Serializable] public sealed class AccountSaveEnvelope
    {
        public string magic = "OVERBURST_ACCOUNT";
        public int schemaVersion = 1;
        public string profileId;
        public long generation;
        public string transactionId;
        public string contentRevision = "1";
        public long writtenAtUtcTicks;
        public byte[] payload;
        public string checksum;
    }

    public sealed class EasySaveAccountStore
    {
        public const int SchemaVersion = 1;
        private readonly string directory;
        private readonly string profileId;
        private AccountSaveEnvelope latest;
        private int latestSlot = -1;
        private bool loaded;
        private readonly byte[][] verifiedBytes = new byte[2][];
        private readonly AccountSaveEnvelope[] verifiedEnvelopes = new AccountSaveEnvelope[2];
        public long Generation => latest?.generation ?? 0;
        public string LastTransactionId => latest?.transactionId;
        // Used by fault-injection tests; product code leaves this unset.
        public Action<string> FaultInjector { get; set; }

        public EasySaveAccountStore(string directory, string profileId = "default")
        {
            this.directory = Path.GetFullPath(directory);
            this.profileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
        }

        private string FilePath(int slot) => Path.Combine(directory, slot == 0 ? "account-A.es3" : "account-B.es3");
        private ES3Settings Settings(int slot) => new ES3Settings(FilePath(slot))
        {
            location = ES3.Location.File,
            encryptionType = ES3.EncryptionType.None,
            compressionType = ES3.CompressionType.None
        };

        public AccountSnapshot Load()
        {
            Directory.CreateDirectory(directory);
            using (AcquireLock()) return LoadCore();
        }

        private FileStream AcquireLock() => new FileStream(Path.Combine(directory, "account.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        private AccountSnapshot LoadCore()
        {
            loaded = false;
            latest = null;
            latestSlot = -1;
            bool any = false;
            for (int i = 0; i < 2; i++)
            {
                if (!File.Exists(FilePath(i))) continue;
                any = true;
                var candidate = TryRead(i);
                if (candidate != null && (latest == null || candidate.generation > latest.generation))
                { latest = candidate; latestSlot = i; }
            }
            if (latest == null)
            {
                if (any) throw new InvalidDataException("No valid account generation. Existing files are preserved.");
                loaded = true;
                return null;
            }
            if (latest.schemaVersion != SchemaVersion) throw new InvalidDataException("Unsupported account schema: " + latest.schemaVersion);
            var snapshot = ES3.Deserialize<AccountSnapshot>(latest.payload);
            if (snapshot == null || snapshot.schemaVersion != latest.schemaVersion || snapshot.profileId != profileId)
                throw new InvalidDataException("Account payload/header mismatch.");
            loaded = true;
            return snapshot;
        }

        public void Save(AccountSnapshot snapshot, string transactionId)
        {
            Directory.CreateDirectory(directory);
            using (AcquireLock()) SaveCore(snapshot, transactionId);
        }

        private void SaveCore(AccountSnapshot snapshot, string transactionId)
        {
            if (!loaded) LoadCore();
            if (snapshot == null || snapshot.schemaVersion != SchemaVersion || snapshot.profileId != profileId || string.IsNullOrWhiteSpace(transactionId))
                throw new InvalidDataException("Invalid account save request.");
            if (latest != null && latest.schemaVersion != SchemaVersion) throw new InvalidDataException("Cannot overwrite a newer schema.");
            for (int slot = 0; slot < 2; slot++)
            {
                if (!File.Exists(FilePath(slot))) continue;
                var onDisk = TryRead(slot);
                if (onDisk != null && (onDisk.generation > Generation || (onDisk.generation == Generation && latest != null && onDisk.checksum != latest.checksum)))
                    throw new IOException("Account was updated by another writer; reload before retrying.");
            }
            Directory.CreateDirectory(directory);
            int target = latestSlot == 0 ? 1 : 0;
            var envelope = new AccountSaveEnvelope
            {
                profileId = profileId, generation = checked(Generation + 1), transactionId = transactionId,
                writtenAtUtcTicks = DateTime.UtcNow.Ticks, payload = ES3.Serialize(snapshot)
            };
            envelope.checksum = Checksum(envelope);
            FaultInjector?.Invoke("before-write");
            try
            {
                // Each slot is one complete account envelope, not a shared key store.
                // Skip merging the obsolete slot (also permits replacing a corrupt
                // inactive generation) while retaining ES3's temp-file commit.
                using (var writer = ES3Writer.Create(Settings(target)))
                {
                    writer.Write<AccountSaveEnvelope>("account", envelope);
                    writer.Save(false);
                }
                FaultInjector?.Invoke("after-write");
            }
            catch
            {
                var uncertain = TryRead(target);
                if (uncertain == null || uncertain.transactionId != transactionId || uncertain.checksum != envelope.checksum) throw;
            }
            var readback = TryRead(target);
            if (readback == null || readback.generation != envelope.generation || readback.transactionId != transactionId || readback.checksum != envelope.checksum)
                throw new IOException("Account save readback failed; prior generation is preserved.");
            latest = readback;
            latestSlot = target;
        }

        private AccountSaveEnvelope TryRead(int slot)
        {
            try
            {
                var settings = Settings(slot);
                // Read the actual file under account.lock on every check. Reuse parsing
                // only for byte-identical, checksum-verified contents, never timestamps.
                byte[] bytes = ES3.LoadRawBytes(settings);
                if (SameBytes(bytes, verifiedBytes[slot])) return verifiedEnvelopes[slot];
                var value = new ES3File(bytes, settings).Load<AccountSaveEnvelope>("account");
                if (value == null || value.magic != "OVERBURST_ACCOUNT" || value.profileId != profileId || value.generation < 1 || value.payload == null || value.checksum != Checksum(value)) return null;
                verifiedBytes[slot] = bytes;
                verifiedEnvelopes[slot] = value;
                return value;
            }
            catch (Exception e) when (e is IOException || e is FormatException || e is ArgumentException || e is InvalidOperationException || e is System.Collections.Generic.KeyNotFoundException)
            { return null; }
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        public static string Checksum(AccountSaveEnvelope e)
        {
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
            using (var sha = SHA256.Create())
            {
                writer.Write(e.magic ?? ""); writer.Write(e.schemaVersion); writer.Write(e.profileId ?? "");
                writer.Write(e.generation); writer.Write(e.transactionId ?? ""); writer.Write(e.contentRevision ?? "");
                writer.Write(e.writtenAtUtcTicks); writer.Write(e.payload?.Length ?? 0);
                if (e.payload != null) writer.Write(e.payload);
                writer.Flush();
                return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
