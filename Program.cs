using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using CommandLine;
using Avro;
using Avro.SchemaGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SchemaRegistry
{
    [Verb("schemagen", HelpText = "Parse the file")]
    class SchemaGenOptions {
        [Option('i', "include", Required = true, Default = null, HelpText = "Includes")]
        public string Includes { get; set; }

        [Option('o', "outfile", Required = false, Default = ".", HelpText = "Output file. . for console")]
        public string Out { get; set; }

        [Option('t', "target", Required = true, Default = null, HelpText = "File name of the type to generate")]
        public string Target { get; set; }

        [Option('c', "converters", Required = true, Default = null, HelpText = "Default converters")]
        public string Converters { get; set; }

    }
    class Program
    {
        static int SchemaGen(SchemaGenOptions opts)
        {

            Dictionary<string,string> defaultConverters = null;
            if (opts.Converters != null)
            {
                defaultConverters = new Dictionary<string, string>();
                var x = opts.Converters.Split(",");
                foreach (var c in x)
                {
                    var y = c.Split(":");
                    if (y.Length != 2)
                    {
                        throw new Exception($"Default converter {c} should be \"CSharpType:AvroType\"");
                    }
                    defaultConverters.Add(y[0],y[1]);
                }
            }
            var graph = new ReferenceGraph<string, JToken>();
            var walker = new SchemaBuilder(defaultConverters, null, graph);

            foreach (var f in opts.Includes.Split(","))
            {
                if (!Directory.Exists(f))
                {
                    Console.Error.WriteLine($"Directory {f} does not exist");
                    continue;
                }
                foreach (var file in Directory.GetFiles(f,"*.cs"))
                {
                    Console.Error.WriteLine($"Reading file {file}");
                    string readText = File.ReadAllText(file);
                    try
                    {
                        var tree = CSharpSyntaxTree.ParseText(readText);
                        walker.Visit(tree.GetRoot());
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"Error parsing file {file}: {e.Message}");
                    }
                }
            }
            var protocolTypes = new JArray();
            var searcher = new ReferenceGraphDepthFirstTraverser<string,JToken>(graph);
            searcher.Visit((n, j)=> {
                if (j==null) 
                {
                    throw new Exception($"Type {n} not found in parsed files");
                }
                protocolTypes.Add(j);
            });
            var protocol = new JObject();
            protocol.Add("protocol", "myProtocol");
            protocol.Add("types", protocolTypes);
            var avroProtocol = Protocol.Parse(protocol.ToString());
            foreach (var schema in avroProtocol.Types)
            {
                if (schema.Fullname == opts.Target)
                {
                    var json = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(schema.ToString()), Formatting.Indented);

                    if (opts.Out != ".")
                    {
                        File.WriteAllText(opts.Out, json);
                    }
                    else
                    {
                        Console.WriteLine(json);
                    }
                }
            }

            return 0;
        }

        // allow mix of C# classes and schema types defined in json
        // not complete
        void ProcessJsonType(JToken jtok, string parent, ReferenceGraph<string, JToken> references)
        {
            switch (jtok)
            {
                case JArray jArray:
                    foreach (var t in jArray.Children())
                    {
                        ProcessJsonType(t, parent, references);
                    }
                break;
                case JObject jObject:
                    var typeProp = jObject.Property("type").Value as JValue;
                    if (typeProp == null)
                    { 
                        return;
                    }
                    var typeVal = typeProp.Value as JToken;
                    if (typeVal == null)
                    {
                        ProcessJsonType(typeProp, parent, references);
                    }
                    else if ((typeProp.Value as JValue).Value as string == "record")
                    {
                        var name = jObject.Property("name").Value as JValue;
                        var props = jObject.Property("properties").Value as JArray;
                        foreach (var p in props)
                        {
                            ProcessJsonType(p, name.Value as string, references);
                        }
                    }
                break;
                case JValue jValue:
                    var val = jValue.Value as string;
                    if ( val == null)
                    {
                        return;
                    }
                    switch (val)
                    {
                        case "string":
                        case "int":
                        case "double":
                        break;
                        default:
                        break;
                    }
                break;
            }
        }
        static int Main(string[] args)
        {
            try
            {
                return CommandLine.Parser.Default.ParseArguments<
                    SchemaGenOptions
                >(args)
                    .MapResult(
                    (SchemaGenOptions opts) => SchemaGen(opts),
                    errs => 1);       
            }
            catch (Exception e) 
            {
                Console.Error.WriteLine($"Caught exception {e.Message}");
                return 1;
            }
        }
    }
}