using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Avro.SchemaGen
{
    public class SchemaPropertyInfo
    {
        private DAG<string, JToken> _typesGraph;
        private Dictionary<string, string> _defaultConverters;
        private Dictionary<string, string> _propertyConverters;
        private Dictionary<string, string> _arrayHelpers;

        public SchemaPropertyInfo(Dictionary<string, string> defaultConverters, Dictionary<string, string> propertyConverters, Dictionary<string, string> arrayHelpers, DAG<string, JToken> typesGraph)
        {
            _defaultConverters = defaultConverters;
            _propertyConverters = propertyConverters;
            _arrayHelpers = arrayHelpers;
            _typesGraph = typesGraph;
        }

        public string RecordName { get; set; }
        public string PropertyName { get; set; }
        public string NewPropertyName { get; set; }
        public bool IsNull { get; set; }
        public bool IsOptional { get; set; }
        public bool IsUnion { get => UnionTypes == null; }

        public List<string> UnionTypes { get; set; }
        public SchemaTypeInfo TypeInfo { get; set; }

        public string Converter { get; set; }

        public string DefaultValue { get; set; }

        public JToken GetFieldSchema()
        {
            JObject field = new JObject();
            if (NewPropertyName == null)
            {
                field.Add("name", PropertyName);
            }
            else
            {
                field.Add("name", NewPropertyName);
            }
            if (IsNull)
            {
                field.Add("type", "null");
            }
            else
            {
                field.Add("type", GetFieldType());
            }

            if (DefaultValue != null)
            {
                field.Add("default", JToken.Load(new JsonTextReader(new StringReader(DefaultValue))));
            }
            else if (IsOptional)
            {
                field.Add("default", "null");
            }
            else if (TypeInfo.Syntax == SchemaTypeInfo.SyntaxType.nullable)
            {
                field.Add("default", "null");
            }

            return field;
        }

        public JToken GetFieldType()
        {
            switch (TypeInfo.Syntax)
            {
                case SchemaTypeInfo.SyntaxType.primitive:
                    return GetPrimitiveType();

                case SchemaTypeInfo.SyntaxType.complex:
                    return GetComplexType();

                case SchemaTypeInfo.SyntaxType.nullable:
                    return GetNullableType();

                case SchemaTypeInfo.SyntaxType.generic:
                    return GetGenericType();

                case SchemaTypeInfo.SyntaxType.array:
                    return GetArrayType();

                default:
                    throw new Exception($"Unknown TypeSyntax {TypeInfo.TypeName}");
            }
        }
        private JToken GetArrayType()
        {
            if (TypeInfo.TypeName == "byte")
            {
                return new JValue("bytes");
            }
            else
            {
                throw new Exception($"Unknown array type {TypeInfo.TypeName.ToString()}");
            }
        }

        private JToken GetPrimitiveType()
        {
            JToken predefinedTypeJson;
            if (UnionTypes == null)
            {
                var predefinedTypeName = GetConvertedTypeName(TypeInfo.TypeName, Converter);
                if (IsPrimitiveType(predefinedTypeName))
                {
                    predefinedTypeJson =  GetPrimitiveTypeJson(IsOptional, TypeInfo.TypeName);
                    if (predefinedTypeJson == null)
                    {
                        throw new Exception($"Predefined type {TypeInfo.TypeName} with no converter");
                    }
                }
                else
                {
                    var complexInfo = new SchemaPropertyInfo(_defaultConverters, _propertyConverters, _arrayHelpers, _typesGraph);
                    complexInfo.RecordName = RecordName;
                    complexInfo.PropertyName = PropertyName;
                    complexInfo.UnionTypes = UnionTypes;
                    complexInfo.TypeInfo = new SchemaTypeInfo();
                    complexInfo.TypeInfo.TypeName = predefinedTypeName;
                    predefinedTypeJson = complexInfo.GetComplexType();
                }
            }
            else
            {
                predefinedTypeJson = new JArray();
                var unionToken = predefinedTypeJson as JArray;
                foreach (var ut in UnionTypes)
                {
                    string typeName = ut;
                    {
                        typeName = GetConvertedTypeName(ut, null);
                    }
                    if (IsPrimitiveType(typeName))
                    {
                        unionToken.Add(GetPrimitiveTypeJson(false, typeName));
                    }
                    else
                    {
                        var unionInfo = new SchemaPropertyInfo(_defaultConverters, _propertyConverters, _arrayHelpers, _typesGraph);
                        unionInfo.RecordName = RecordName;
                        unionInfo.PropertyName = PropertyName;
                        unionInfo.TypeInfo = new SchemaTypeInfo();
                        unionInfo.TypeInfo.TypeName = typeName;
                        unionToken.Add(unionInfo.GetComplexType());
                    }
                }
            }
            return predefinedTypeJson;
        }

        private JToken GetNullableType()
        {
            var nullable = new JArray();
            nullable.Add("null");
            var nullableInfo = new SchemaPropertyInfo(_defaultConverters, _propertyConverters, _arrayHelpers, _typesGraph);
            nullableInfo.RecordName = RecordName;
            nullableInfo.PropertyName = PropertyName;
            nullableInfo.UnionTypes = UnionTypes;
            nullableInfo.TypeInfo = new SchemaTypeInfo();
            nullableInfo.TypeInfo.TypeName = TypeInfo.TypeName;
            nullableInfo.TypeInfo.Syntax = SchemaTypeInfo.SyntaxType.primitive;
            nullable.Add(nullableInfo.GetFieldType());
            return nullable;
        }

        private JToken GetGenericType()
        {
            JToken token;
            switch (TypeInfo.TypeName)
            {
                case "List":
                    var array = new JObject();
                    array.Add("type", "array");
                    var listInfo = new SchemaPropertyInfo(_defaultConverters, _propertyConverters, _arrayHelpers, _typesGraph);
                    listInfo.RecordName = RecordName;
                    listInfo.PropertyName = PropertyName;
                    listInfo.UnionTypes = UnionTypes;
                    listInfo.TypeInfo = TypeInfo.GenericParameters[0];
                    array.Add("items", listInfo.GetFieldType());
                    token = array;
                    break;
                case "Dictionary":
                    var map = new JObject();
                    map.Add("type", "map");
                    var mapInfo = new SchemaPropertyInfo(_defaultConverters, _propertyConverters, _arrayHelpers, _typesGraph);
                    mapInfo.RecordName = RecordName;
                    mapInfo.PropertyName = PropertyName;
                    mapInfo.UnionTypes = UnionTypes;
                    mapInfo.TypeInfo = TypeInfo.GenericParameters[1];
                    map.Add("values", mapInfo.GetFieldType());
                    token = map;
                    break;
                default:
                    if (_arrayHelpers != null && _arrayHelpers.ContainsKey(TypeInfo.TypeName))
                    {
                        var helpedArray = new JObject();
                        helpedArray.Add("type", "array");
                        helpedArray.Add("helper", _arrayHelpers[TypeInfo.TypeName]);
                        var helperListInfo = new SchemaPropertyInfo(_defaultConverters, _propertyConverters, _arrayHelpers, _typesGraph);
                        helperListInfo.RecordName = RecordName;
                        helperListInfo.PropertyName = PropertyName;
                        helperListInfo.UnionTypes = UnionTypes;
                        helperListInfo.TypeInfo = TypeInfo.GenericParameters[0];
                        helpedArray.Add("items", helperListInfo.GetFieldType());
                        token = helpedArray;
                    }
                    else
                    {
                        throw new Exception($"Unknown generic type {TypeInfo.TypeName}");
                    }

                    break;
            }

            if (IsOptional)
            {
                var optToken = new JArray();
                optToken.Add("null");
                optToken.Add(token);
                return optToken;
            }
            else
            {
                return token;
            }
        }



        private JToken GetComplexType()
        {
            JToken identifierToken;

            if (UnionTypes == null)
            {
                string typeName = TypeInfo.TypeName;
                {
                    typeName = GetConvertedTypeName(TypeInfo.TypeName, Converter);
                }
                if (IsPrimitiveType(typeName))
                {
                    return GetPrimitiveTypeJson(IsOptional, typeName);
                }
                if (typeName != "null")
                {
                    _typesGraph.AddEdge(RecordName, typeName);
                }
                if (IsOptional)
                {
                    identifierToken = new JArray();
                    ((JArray)identifierToken).Add("null");
                    ((JArray)identifierToken).Add(typeName);
                }
                else
                {
                    identifierToken = new JValue(typeName);
                }
            }
            else
            {
                identifierToken = new JArray();
                var unionToken = identifierToken as JArray;
                foreach (var ut in UnionTypes)
                {
                    string typeName = ut;
                    {
                        typeName = GetConvertedTypeName(ut, null);
                    }
                    if (IsPrimitiveType(typeName))
                    {
                        unionToken.Add(GetPrimitiveTypeJson(false, typeName));
                    }
                    else
                    {
                        var unionInfo = new SchemaPropertyInfo(_defaultConverters, _propertyConverters, _arrayHelpers, _typesGraph);
                        unionInfo.RecordName = RecordName;
                        unionInfo.PropertyName = PropertyName;
                        unionInfo.TypeInfo = new SchemaTypeInfo();
                        unionInfo.TypeInfo.TypeName = typeName;
                        unionToken.Add(unionInfo.GetComplexType());
                    }
                }
            }

            return identifierToken;
        }
        private static bool IsPrimitiveType(string predefinedTypeName)
        {
            switch (predefinedTypeName)
            {
                case "bool":
                case "Boolean":
                case "int":
                case "long":
                case "float":
                case "double":
                case "string":
                    return true;
                default:
                    return false;
            }
        }

        private static JToken GetPrimitiveTypeJson(bool isOptional, string predefinedTypeName)
        {
            switch (predefinedTypeName)
            {
                case "bool":
                case "Boolean":
                    return new JValue("boolean");
                case "int":
                    return new JValue("int");
                case "long":
                    return new JValue("long");
                case "float":
                    return new JValue("float");
                case "double":
                    return new JValue("double");
                case "string":
                    JToken stringToken;
                    if (isOptional)
                    {
                        stringToken = new JArray();
                        ((JArray)stringToken).Add("null");
                        ((JArray)stringToken).Add(new JValue("string"));
                    }
                    else
                    {
                        stringToken = new JValue("string");
                    }

                    return stringToken;
                default:
                    return null;
            }
        }
        private string GetConvertedTypeName(string typeName, string converter)
        {
            if (converter != null) 
            {
                if (_propertyConverters == null || !_propertyConverters.ContainsKey(converter))
                {
                    throw new Exception($"Unknown converter {converter}. Pass these into SchemaBuilder constructor");
                }
                typeName = _propertyConverters[converter];
            }
            else
            {
                if (_defaultConverters != null && _defaultConverters.ContainsKey(typeName))
                {
                    typeName = _defaultConverters[typeName];
                }
            }

            return typeName;
        }
    }
}
