using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Avro.SchemaGen
{
    public class ProtocolTypes
    {
        private DAG<string, JToken> _typesGraph;
        private Dictionary<string, string> _defaultConverters;
        private Dictionary<string, string> _propertyConverters;
        private Dictionary<string, string> _arrayHelpers;

        public DAG<string, JToken> TypesGraph
        {
            get => _typesGraph;
            set => _typesGraph = value;
        }

        public ProtocolTypes(Dictionary<string, string> defaultConverters, Dictionary<string, string> propertyConverters, Dictionary<string, string> arrayHelpers, DAG<string, JToken> graph)
        {
            if (graph == null)
            {
                _typesGraph = new DAG<string, JToken>();
            }
            else
            {
                _typesGraph = graph;
            }
            _defaultConverters = defaultConverters;
            _propertyConverters = propertyConverters;
            _arrayHelpers = arrayHelpers;
        }

        public void AddSchema( SchemaInfo schema )
        {
            _typesGraph.AddOrUpdateNode(schema.Name, schema.Schema);
        }

        public SchemaEnumInfo CreateEnum(string namespaceName, string enumName)
        {
            return new SchemaEnumInfo(namespaceName, enumName, _typesGraph);
        }

        public SchemaRecordInfo CreateRecord(string namespaceName, string recordName)
        {
            return new SchemaRecordInfo(namespaceName, recordName, _typesGraph);
        }

        public SchemaPropertyInfo CreateProperty()
        {
            return new SchemaPropertyInfo(_defaultConverters, _propertyConverters, _arrayHelpers, _typesGraph);
        }

        public Protocol GetProtocol()
        {
            var protocolTypes = new JArray();
            var searcher = new DAGDepthFirstTraverser<string,JToken>(_typesGraph);
            searcher.Traverse((n, j)=> {
                if (j==null) 
                {
                    throw new Exception($"Type {n} not found in parsed files");
                }
                protocolTypes.Add(j);
            });
            Console.WriteLine(protocolTypes.ToString());
            var protocol = new JObject();
            protocol.Add("protocol", "myProtocol");
            protocol.Add("types", protocolTypes);
            //Console.WriteLine(protocol.ToString());
            return Protocol.Parse(protocol.ToString());
        }
    }
}
