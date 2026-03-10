namespace NSFinTech.Api.Modules.Support.DTOs;

public sealed record ExportDownloadPayload(
    string FileName,
    string ContentType,
    byte[] Bytes);