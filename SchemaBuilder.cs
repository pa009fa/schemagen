using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avro.SchemaGen
{
    public class SchemaBuilder : CSharpSyntaxWalker
    {
        private ReferenceGraph<string, JToken> _references;
        private string _className = string.Empty;
        private Dictionary<string, string> _defaultConverters;
        private Dictionary<string, string> _arrayHelpers;

        public ReferenceGraph<string, JToken> References
        {
            get => _references;
            set => _references = value;
        }

        public SchemaBuilder(Dictionary<string, string> defaultConverters, Dictionary<string, string> arrayHelpers, ReferenceGraph<string, JToken> graph)
            : base(SyntaxWalkerDepth.Node)
        {
            if (graph == null)
            {
                _references = new ReferenceGraph<string, JToken>();
            }
            else
            {
                _references = graph;
            }

            _defaultConverters = defaultConverters;
            _arrayHelpers = arrayHelpers;
        }

        public static string GetNameSpace(SyntaxNode node)
        {
            while (node != null)
            {
                var ns = node as NamespaceDeclarationSyntax;
                if (ns != null)
                {
                    foreach (var member in ns.Members)
                    {
                        member.ToString();
                    }

                    return ((QualifiedNameSyntax)ns.Name).ToString();
                }

                node = node.Parent;
            }

            return null;
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            if (!IsPublic(node))
            {
                return;
            }

            var ns = GetNameSpace(node);
            var schema = new JObject();
            schema.Add("type", "enum");
            schema.Add("name", node.Identifier.Text);
            if (ns != null)
            {
                schema.Add("namespace", ns);
            }

            var symbols = new JArray();
            foreach (var member in node.Members)
            {
                switch (member)
                {
                    case EnumMemberDeclarationSyntax enumMember:
                        symbols.Add(enumMember.Identifier.ToString());
                    break;
                }
            }

            schema.Add("symbols", symbols);
            _references.AddOrUpdateClass(node.Identifier.Text, schema);
            base.VisitEnumDeclaration(node);
        }

        public bool IsPublic(BaseTypeDeclarationSyntax node)
        {
            foreach (var modifier in node.Modifiers)
            {
                if (modifier.ValueText == "public")
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsPublic(BasePropertyDeclarationSyntax node)
        {
            foreach (var modifier in node.Modifiers)
            {
                if (modifier.ValueText == "public")
                {
                    return true;
                }
            }

            return false;
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (!IsPublic(node))
            {
                return;
            }

            var ns = GetNameSpace(node);
            var recordName = node.Identifier.Text;

            JObject schema = new JObject();
            schema.Add("type", "record");
            schema.Add("name", recordName);
            if (ns != null)
            {
                schema.Add("namespace", ns);
            }

            _references.AddOrUpdateClass(recordName, schema);
            JArray fields = new JArray();
            foreach (var member in node.Members)
            {
                switch (member)
                {
                    case PropertyDeclarationSyntax property:
                        if (!IsPublic(property))
                        {
                            continue;
                        }

                        if (member.HasStructuredTrivia)
                        {
                            var sb = new StringBuilder();
                            foreach (var xmlComment in member.GetLeadingTrivia())
                            {
                                var trim = Regex.Replace(xmlComment.ToString(), @"^\s*///*\s*", string.Empty);
                                sb.AppendLine();
                            }
                        }

                        string renamedProperty = null;
                        string converter = null;
                        bool isOptional = false;
                        bool isNull = false;
                        string defaultValue = null;
                        foreach (var attrList in property.AttributeLists)
                        {
                            foreach (var attr in attrList.Attributes)
                            {
                                var ins = attr.Name as IdentifierNameSyntax;
                                if (ins != null)
                                {
                                    if (ins.Identifier.ValueText == "AvroField")
                                    {
                                        (renamedProperty, converter) = AvroFieldAttribute(attr);
                                    }

                                    if (ins.Identifier.ValueText == "SchemaOptional")
                                    {
                                        isOptional = true;
                                    }

                                    if (ins.Identifier.ValueText == "SchemaDefault")
                                    {
                                        defaultValue = DefaultValueAttribute(defaultValue, attr);
                                    }

                                    if (ins.Identifier.ValueText == "SchemaNull")
                                    {
                                        isNull = true;
                                    }

                                }
                            }
                        }

                        fields.Add(GetFieldSchema(recordName, property, renamedProperty, converter, isOptional, isNull, defaultValue));
                        break;
                }
            }

            schema.Add("fields", fields);
            _references.AddOrUpdateClass(recordName, schema);
            base.VisitClassDeclaration(node);
        }

        private static string DefaultValueAttribute(string defaultValue, AttributeSyntax attr)
        {
            foreach (var args in attr.ArgumentList.Arguments)
            {
                switch (args.Expression)
                {
                    case LiteralExpressionSyntax attrLiteral:
                        defaultValue = attrLiteral.Token.ValueText;
                        break;
                    default:
                        throw new Exception($"Unknown attribute expression type {args.Expression} at line {args.Expression.GetLocation().GetLineSpan().StartLinePosition.Line}");
                }
            }

            return defaultValue;
        }

#pragma warning disable SA1008 // Doesnt deal with tuples
        private static (string, string) AvroFieldAttribute(AttributeSyntax attr)
        {
            string renamedProperty = null;
            string converter = null;
            foreach (var args in attr.ArgumentList.Arguments)
            {
                switch (args.Expression)
                {
                    case LiteralExpressionSyntax attrLiteral:
                        renamedProperty = attrLiteral.Token.ValueText;
                        break;
                    case TypeOfExpressionSyntax attrTypeOf:
                        var typeSyntax = attrTypeOf.Type as IdentifierNameSyntax;
                        if (typeSyntax == null)
                        {
                            throw new Exception($"Cant process identifier in AvroField attribute {args.Expression} at line {typeSyntax.GetLocation().GetLineSpan().StartLinePosition.Line}");
                        }
                        converter = typeSyntax.Identifier.ValueText;
                        break;
                    default:
                        throw new Exception($"Unknown attribute expression type {args.Expression} at line {args.Expression.GetLocation().GetLineSpan().StartLinePosition.Line}");
                }
            }

            return (renamedProperty, converter);
        }
#pragma warning restore SA1008 // Doesnt deal with tuples

        private JToken GetFieldSchema(string recordName, PropertyDeclarationSyntax property, string renamedProperty, string converter, bool isOptional, bool isNull, string defaultValue)
        {
            JObject field = new JObject();
            if (renamedProperty == null)
            {
                field.Add("name", property.Identifier.Text);
            }
            else
            {
                field.Add("name", renamedProperty);
            }
            if (isNull)
            {
                field.Add("type", "null");
            }
            else
            {
                field.Add("type", GetType(recordName, property.Type, converter, isOptional));
            }

            if (defaultValue != null)
            {
                field.Add("default", JToken.Load(new JsonTextReader(new StringReader(defaultValue))));
            }

            return field;
        }

        private JToken GetType(string recordName, TypeSyntax typeSyntax, string converter, bool isOptional)
        {
            switch (typeSyntax)
            {
                case PredefinedTypeSyntax predefinedType:
                    return GetPrimitiveType(isOptional, predefinedType);

                case IdentifierNameSyntax identifierName:
                    return GetComplexType(recordName, isOptional, identifierName);

                case NullableTypeSyntax nullableType:
                    return GetNullableType(recordName, nullableType);

                case GenericNameSyntax genericType:
                    return GetGenericType(recordName, isOptional, genericType);

                case ArrayTypeSyntax arrayType:
                    return GetArrayType(arrayType);

                default:
                    throw new Exception($"Unknown TypeSyntax {typeSyntax.GetType()} at line {typeSyntax.GetLocation().GetLineSpan().StartLinePosition.Line}");
            }
        }

        private static JToken GetArrayType(ArrayTypeSyntax arrayType)
        {
            var elementTypeSyntax = arrayType.ElementType as PredefinedTypeSyntax;
            if (elementTypeSyntax != null && elementTypeSyntax.Keyword.ValueText == "byte")
            {
                return new JValue("bytes");
            }
            else
            {
                throw new Exception($"Unknown array type {arrayType.ToString()} at line {arrayType.GetLocation().GetLineSpan().StartLinePosition.Line}");
            }
        }

        private JToken GetNullableType(string recordName, NullableTypeSyntax nullableType)
        {
            var nullable = new JArray();
            nullable.Add("null");
            nullable.Add(GetType(recordName, nullableType.ElementType, converter: null, isOptional: false));
            return nullable;
        }

        private JToken GetComplexType(string recordName, bool isOptional, IdentifierNameSyntax identifierName)
        {
            var typeName = GetConvertedTypeName(identifierName.Identifier.Text);
            if (IsPrimitiveType(typeName))
            {
                return GetPrimitiveTypeJson(isOptional, typeName);
            }
            _references.AddReference(recordName, identifierName.Identifier.Text);
            JToken identifierToken;
            if (isOptional)
            {
                identifierToken = new JArray();
                ((JArray)identifierToken).Add("null");
                ((JArray)identifierToken).Add(identifierName.Identifier.Text);
            }
            else
            {
                identifierToken = new JValue(identifierName.Identifier.Text);
            }

            return identifierToken;
        }

        private JToken GetGenericType(string recordName, bool isOptional, GenericNameSyntax genericType)
        {
            JToken token;
            switch (genericType.Identifier.Text)
            {
                case "List":
                    var array = new JObject();
                    array.Add("type", "array");
                    array.Add("items", GetType(recordName, genericType.TypeArgumentList.Arguments.First(), converter: null, isOptional: false));
                    token = array;
                    break;
                case "Dictionary":
                    var map = new JObject();
                    map.Add("type", "map");
                    map.Add("items", GetType(recordName, genericType.TypeArgumentList.Arguments[1], converter: null, isOptional: false));
                    token = map;
                    break;
                default:
                    if (_arrayHelpers != null && _arrayHelpers.ContainsKey(genericType.Identifier.Text))
                    {
                        var helpedArray = new JObject();
                        helpedArray.Add("type", "array");
                        helpedArray.Add("helper", _arrayHelpers[genericType.Identifier.Text]);
                        helpedArray.Add("items", GetType(recordName, genericType.TypeArgumentList.Arguments.First(), converter: null, isOptional: false));
                        token = helpedArray;
                    }
                    else
                    {
                        throw new Exception($"Unknown generic type {genericType.Identifier.Text} at line {genericType.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line}");
                    }

                    break;
            }

            if (isOptional)
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

        private string GetConvertedTypeName(string typeName)
        {
            if (_defaultConverters != null && _defaultConverters.ContainsKey(typeName))
            {
                typeName = _defaultConverters[typeName];
            }

            return typeName;
        }

        private JToken GetPrimitiveType(bool isOptional, PredefinedTypeSyntax predefinedType)
        {
            var predefinedTypeName = GetConvertedTypeName(predefinedType.Keyword.ValueText);
            var predefinedTypeJson =  GetPrimitiveTypeJson(isOptional, predefinedTypeName);
            if (predefinedTypeJson==null)
            {
                throw new Exception($"Predefined type {predefinedType} with no converter at line {predefinedType.GetLocation().GetLineSpan().StartLinePosition.Line}");
            }
            return predefinedTypeJson;
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
    }
}
