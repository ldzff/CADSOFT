using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using IxMilia.Dxf; // For DxfPoint, DxfVector

namespace RobTeach.Services
{
    public class DxfPointJsonConverter : JsonConverter<DxfPoint>
    {
        public override DxfPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token");
            }

            double x = 0, y = 0, z = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new DxfPoint(x, y, z);
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token");
                }

                string propertyName = reader.GetString();
                reader.Read(); // Move to property value

                switch (propertyName?.ToUpperInvariant()) // Case-insensitive matching
                {
                    case "X":
                        x = reader.GetDouble();
                        break;
                    case "Y":
                        y = reader.GetDouble();
                        break;
                    case "Z":
                        z = reader.GetDouble();
                        break;
                    default:
                        // Handle or ignore unknown properties
                        reader.Skip();
                        break;
                }
            }
            throw new JsonException("Expected EndObject token");
        }

        public override void Write(Utf8JsonWriter writer, DxfPoint value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteNumber("Z", value.Z);
            writer.WriteEndObject();
        }
    }

    public class DxfVectorJsonConverter : JsonConverter<DxfVector>
    {
        public override DxfVector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token");
            }

            double x = 0, y = 0, z = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new DxfVector(x, y, z);
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token");
                }

                string propertyName = reader.GetString();
                reader.Read(); // Move to property value

                switch (propertyName?.ToUpperInvariant()) // Case-insensitive matching
                {
                    case "X":
                        x = reader.GetDouble();
                        break;
                    case "Y":
                        y = reader.GetDouble();
                        break;
                    case "Z":
                        z = reader.GetDouble();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            throw new JsonException("Expected EndObject token");
        }

        public override void Write(Utf8JsonWriter writer, DxfVector value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteNumber("Z", value.Z);
            writer.WriteEndObject();
        }
    }
}
