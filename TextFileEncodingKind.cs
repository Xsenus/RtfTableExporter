using System.Text;

namespace RtfTableExporter;

internal enum TextFileEncodingKind
{
    Cp1251,
    Utf8,
    Utf8Bom,
}

internal static class TextFileEncodingKindExtensions
{
    public static Encoding GetEncoding(this TextFileEncodingKind encodingKind)
        => encodingKind switch
        {
            TextFileEncodingKind.Cp1251 => Encoding.GetEncoding(
                1251,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback),
            TextFileEncodingKind.Utf8 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            TextFileEncodingKind.Utf8Bom => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
            _ => throw new InvalidOperationException($"Unsupported encoding kind: {encodingKind}."),
        };

    public static string ToCliValue(this TextFileEncodingKind encodingKind)
        => encodingKind switch
        {
            TextFileEncodingKind.Cp1251 => "cp1251",
            TextFileEncodingKind.Utf8 => "utf8",
            TextFileEncodingKind.Utf8Bom => "utf8-bom",
            _ => throw new InvalidOperationException($"Unsupported encoding kind: {encodingKind}."),
        };
}
