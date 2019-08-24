using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Avro.SchemaGen
{
    public class SchemaInfo
    {
        public JObject Schema = new JObject();
        protected DAG<string, JToken> _typesGraph;

        public string Name { get; set; }
        public SchemaInfo(DAG<string, JToken> typesGraph)
        {
            _typesGraph = typesGraph;
        }
    }
    public class SchemaEnumInfo : SchemaInfo
    {
        private JArray _symbols = new JArray();
        public SchemaEnumInfo(string nameSpace, string enumName, DAG<string, JToken> typesGraph) : base(typesGraph)
        {
            Name = enumName;
            NameSpace = nameSpace;
            Schema.Add("type", "enum");
            Schema.Add("name", Name);
            if (nameSpace != null)
            {
                Schema.Add("namespace", nameSpace);
            }
            Schema.Add("symbols", _symbols);
        }

        public string NameSpace { get; set; }

        public void AddSymbol( string symbol )
        {
            _symbols.Add(symbol);
        }
    }
    public class SchemaRecordInfo : SchemaInfo
    {
        private JArray _fields = new JArray();
        public SchemaRecordInfo(string nameSpace, string recordName, DAG<string, JToken> typesGraph) : base(typesGraph)
        {
            Name = recordName;
            NameSpace = nameSpace;
            Schema.Add("type", "record");
            Schema.Add("name", recordName);
            if (nameSpace != null)
            {
                Schema.Add("namespace", nameSpace);
            }

            _typesGraph.AddOrUpdateNode(recordName, Schema);
            Schema.Add("fields", _fields);
        }

        public string RecordName { get; set; }
        public string NameSpace { get; set; }

        public void AddField(SchemaPropertyInfo info)
        {
            _fields.Add(info.GetFieldSchema());
        }
    }
}