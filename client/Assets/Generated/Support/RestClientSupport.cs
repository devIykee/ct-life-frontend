// Hand-written support types for the openapi-generator 7.25.0 `csharp` models in ../Rest.
// The contract's codegen runs with supportingFiles=false (models only, contract.md section 1), so the
// helper types the models reference are provided here. This file lives outside ../Rest so that
// regenerating the models never deletes it. Shapes follow the generator's own templates.
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace CTLife.Rest.Client
{
    /// <summary>Formatter for 'date' (not 'date-time') OpenAPI properties.</summary>
    public class OpenAPIDateConverter : IsoDateTimeConverter
    {
        public OpenAPIDateConverter()
        {
            DateTimeFormat = "yyyy'-'MM'-'dd";
        }
    }

    /// <summary>Multipart file parameter. CT Life uploads (Phase 5) use the hand-written RestClient instead.</summary>
    public class FileParameter
    {
        public string Name { get; set; } = "no_name_set";
        public string ContentType { get; set; } = "application/octet-stream";
        public Stream Content { get; set; }

        public FileParameter(Stream content) { Content = content; }
        public FileParameter(string filename, Stream content) { Name = filename; Content = content; }
        public FileParameter(string filename, string contentType, Stream content)
        {
            Name = filename;
            ContentType = contentType;
            Content = content;
        }

        public static implicit operator FileParameter(Stream s) => new FileParameter(s);
    }
}

namespace CTLife.Rest.Model
{
    /// <summary>Base class for oneOf/anyOf schemas.</summary>
    public abstract partial class AbstractOpenAPISchema
    {
        public static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { OverrideSpecifiedNames = false }
            }
        };

        public static readonly JsonSerializerSettings AdditionalPropertiesSerializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { OverrideSpecifiedNames = false }
            }
        };

        public abstract Object ActualInstance { get; set; }
        public bool IsNullable { get; protected set; }
        public string SchemaType { get; protected set; }
        public abstract string ToJson();
    }
}
