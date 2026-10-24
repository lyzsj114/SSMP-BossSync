using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSMP.Serialization;

/// <summary>Compound save payloads stay JSON on the server, so it needs no game assemblies.</summary>
internal static class CompoundSaveCodec {
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal const int MaxPayloadBytes = ushort.MaxValue;

    internal static JsonSerializer CreateSerializer() => JsonSerializer.Create(new JsonSerializerSettings {
        TypeNameHandling = TypeNameHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        MaxDepth = 64,
        DateParseHandling = DateParseHandling.None
    });

    public static byte[] Encode(object? value) {
        var token = value as JToken ?? (value == null ? JValue.CreateNull() : JToken.FromObject(value, CreateSerializer()));
        var bytes = Utf8.GetBytes(token.ToString(Formatting.None));
        if (bytes.Length > MaxPayloadBytes) {
            throw new InvalidDataException($"Compound save payload exceeds {MaxPayloadBytes} bytes ({bytes.Length})");
        }
        return bytes;
    }

    public static JToken Decode(byte[] bytes) {
        if (bytes.Length == 0 || bytes.Length > MaxPayloadBytes) {
            throw new InvalidDataException($"Invalid compound save payload length: {bytes.Length}");
        }
        using var reader = new JsonTextReader(new StringReader(Utf8.GetString(bytes))) {
            MaxDepth = 64, DateParseHandling = DateParseHandling.None
        };
        var token = JToken.ReadFrom(reader);
        if (reader.Read()) throw new InvalidDataException("Trailing content in compound save payload");
        return token;
    }

    // The type comes from a local PlayerData FieldInfo, never from a remote type name.
    public static object? ToGameValue(JToken value, Type fieldType) =>
        value.Type == JTokenType.Null ? null : value.ToObject(fieldType, CreateSerializer());
}
