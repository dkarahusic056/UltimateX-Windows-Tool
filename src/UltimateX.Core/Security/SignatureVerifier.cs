using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace UltimateX.Core.Security;

public enum SignatureStatus
{
    /// <summary>Gültige Authenticode-Signatur, Kette vertrauenswürdig.</summary>
    Valid,
    /// <summary>Keine eingebettete Signatur. Hinweis: Windows-Systemdateien sind oft katalog-signiert.</summary>
    NotSigned,
    /// <summary>Signatur vorhanden, aber Datei verändert (Hash stimmt nicht) – Manipulationsverdacht.</summary>
    Tampered,
    /// <summary>Signatur vorhanden, aber Zertifikat nicht vertrauenswürdig, abgelaufen oder widerrufen.</summary>
    Untrusted,
    /// <summary>Datei fehlt oder konnte nicht gelesen werden.</summary>
    Error,
}

public sealed record SignatureInfo(SignatureStatus Status, string? Publisher, string? Thumbprint, int RawResult)
{
    public string StatusText => Status switch
    {
        SignatureStatus.Valid => "Gültig signiert",
        SignatureStatus.NotSigned => "Keine eingebettete Signatur",
        SignatureStatus.Tampered => "Signatur ungültig – Datei verändert",
        SignatureStatus.Untrusted => "Zertifikat nicht vertrauenswürdig",
        _ => "Prüfung nicht möglich",
    };
}

/// <summary>
/// Authenticode-Prüfung über die offizielle Windows-API WinVerifyTrust (§62, §66, §78).
/// Revocation wird nur aus dem Cache geprüft, damit die Prüfung offline funktioniert
/// und keine Netzwerkzugriffe ohne Wissen des Benutzers stattfinden (§106).
/// </summary>
public static class SignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
    private const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
    private const int CRYPT_E_FILE_ERROR = unchecked((int)0x80092003);

    public static SignatureInfo Verify(string filePath)
    {
        if (!File.Exists(filePath)) return new(SignatureStatus.Error, null, null, 0);

        int result;
        try
        {
            result = WinVerifyTrustFile(filePath);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            return new(SignatureStatus.Error, null, null, 0);
        }

        var status = result switch
        {
            0 => SignatureStatus.Valid,
            TRUST_E_NOSIGNATURE or TRUST_E_SUBJECT_FORM_UNKNOWN or TRUST_E_PROVIDER_UNKNOWN => SignatureStatus.NotSigned,
            TRUST_E_BAD_DIGEST => SignatureStatus.Tampered,
            CRYPT_E_FILE_ERROR => SignatureStatus.Error,
            _ => SignatureStatus.Untrusted,
        };

        string? publisher = null, thumbprint = null;
        if (status is SignatureStatus.Valid or SignatureStatus.Untrusted or SignatureStatus.Tampered)
        {
            try
            {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile ist in .NET 8 der Standardweg zum Lesen des Signaturzertifikats.
                using var raw = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
                using var cert = new X509Certificate2(raw);
                publisher = cert.GetNameInfo(X509NameType.SimpleName, false);
                thumbprint = cert.Thumbprint;
            }
            catch (CryptographicException)
            {
                // Zertifikat nicht lesbar – Status bleibt wie von WinVerifyTrust gemeldet.
            }
        }

        return new(status, publisher, thumbprint, result);
    }

    public static string Sha256(string filePath) => HashFile(filePath, SHA256.Create());
    public static string Sha512(string filePath) => HashFile(filePath, SHA512.Create());

    /// <summary>MD5 nur zur Legacy-Kompatibilität (z. B. Abgleich alter Prüfsummen). Nicht kollisionsresistent.</summary>
#pragma warning disable CA5351 // Bewusst: Legacy-Kompatibilität, in der UI als unsicher markiert (§79).
    public static string Md5Legacy(string filePath) => HashFile(filePath, MD5.Create());
#pragma warning restore CA5351

    private static string HashFile(string filePath, HashAlgorithm algorithm)
    {
        using (algorithm)
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
        {
            return Convert.ToHexString(algorithm.ComputeHash(stream));
        }
    }

    // ---------------- P/Invoke ----------------

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

    private static int WinVerifyTrustFile(string filePath)
    {
        var pathPtr = Marshal.StringToHGlobalUni(filePath);
        var fileInfoPtr = IntPtr.Zero;
        var dataPtr = IntPtr.Zero;
        try
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = pathPtr,
            };
            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = fileInfoPtr,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
            };
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
            Marshal.StructureToPtr(data, dataPtr, false);

            var action = GenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

            // State-Handle immer schließen.
            data = Marshal.PtrToStructure<WINTRUST_DATA>(dataPtr);
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            Marshal.StructureToPtr(data, dataPtr, false);
            WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

            return result;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            if (fileInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }
}
