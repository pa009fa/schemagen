using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avro.SchemaGen
{
    public class StaticCodeBuilder : CSharpSyntaxWalker
    {
        ProtocolTypes _protocolTypes;
        public StaticCodeBuilder(ProtocolTypes protocolTypes)
            : base(SyntaxWalkerDepth.Node)
        {
            _protocolTypes = protocolTypes;
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
            var enumSchema = _protocolTypes.CreateEnum(ns, node.Identifier.Text);

            foreach (var member in node.Members)
            {
                switch (member)
                {
                    case EnumMemberDeclarationSyntax enumMember:
                        enumSchema.AddSymbol(enumMember.Identifier.ToString());
                    break;
                }
            }

            _protocolTypes.AddSchema(enumSchema);
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
            var record = _protocolTypes.CreateRecord(ns, recordName);

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

                        var info = _protocolTypes.CreateProperty();
                        info.RecordName = recordName;

                        foreach (var attrList in property.AttributeLists)
                        {
                            foreach (var attr in attrList.Attributes)
                            {
                                var ins = attr.Name as IdentifierNameSyntax;
                                if (ins != null)
                                {
                                    if (ins.Identifier.ValueText == "AvroField")
                                    {
                                        (info.NewPropertyName, info.Converter) = GetAvroFieldFromAttribute(attr);
                                    }

                                    if (ins.Identifier.ValueText == "AvroOptional")
                                    {
                                        if (info.IsUnion)
                                        {
                                            throw new Exception($"Dont use SchemaOptional and SchemaUnion attributes together at line {attr.GetLocation().GetLineSpan().StartLinePosition.Line}");
                                        }
                                        info.IsOptional = true;
                                    }

                                    if (ins.Identifier.ValueText == "AvroDefault")
                                    {

                                        info.DefaultValue = GetDefaultValueFromAttribute(attr);
                                    }

                                    if (ins.Identifier.ValueText == "AvroNull")
                                    {
                                        info.IsNull = true;
                                    }

                                    if (ins.Identifier.ValueText == "AvroUnion")
                                    {
                                        if (info.IsOptional)
                                        {
                                            throw new Exception($"Dont use SchemaOptional and SchemaUnion attributes together at line {attr.GetLocation().GetLineSpan().StartLinePosition.Line}");
                                        }
                                        info.UnionTypes = new List<string>();

                                        foreach (var args in attr.ArgumentList.Arguments)
                                        {
                                            info.UnionTypes.Add(GetUnionTypeFromAttribute(args));
                                        }
                                    }
                                }
                            }
                        }
                        info.PropertyName = property.Identifier.Text;
                        info.TypeInfo = GetSchemaTypeInfo(property.Type);
                        record.AddField(info);
                        break;
                }
            }
            _protocolTypes.AddSchema(record);
            base.VisitClassDeclaration(node);
        }

        private static SchemaTypeInfo GetSchemaTypeInfo(TypeSyntax typeSyntax)
        {
            var info = new SchemaTypeInfo();

            switch (typeSyntax)
            {
                case PredefinedTypeSyntax predefinedType:
                    info.TypeName = predefinedType.Keyword.ValueText;
                    info.Syntax = SchemaTypeInfo.SyntaxType.primitive;
                    break;
                case IdentifierNameSyntax identifierName:
                    info.Syntax = SchemaTypeInfo.SyntaxType.complex;
                    info.TypeName = identifierName.Identifier.Text;
                    break;
                case NullableTypeSyntax nullableType:
                    info.Syntax = SchemaTypeInfo.SyntaxType.nullable;
                    var np = nullableType.ElementType as PredefinedTypeSyntax;
                    info.TypeName = np.Keyword.ValueText;
                    break;
                case GenericNameSyntax genericType:
                    info.TypeName = genericType.Identifier.Text;
                    info.Syntax = SchemaTypeInfo.SyntaxType.generic;
                    info.GenericParameters = new List<SchemaTypeInfo>();
                    foreach (var a in genericType.TypeArgumentList.Arguments)
                    {
                        info.GenericParameters.Add(GetSchemaTypeInfo(a));
                    }
                    break;
                case ArrayTypeSyntax arrayType:
                    info =  GetSchemaTypeInfo(arrayType.ElementType);
                    info.Syntax = SchemaTypeInfo.SyntaxType.array;
                    break;
                default:
                    throw new Exception($"Unknown TypeSyntax {typeSyntax.GetType()} at line {typeSyntax.GetLocation().GetLineSpan().StartLinePosition.Line}");
            }
            return info;
        }

        private string GetUnionTypeFromAttribute(AttributeArgumentSyntax args)
        {

            switch (args.Expression)
            {
                case LiteralExpressionSyntax attrLiteral:
                    var literalValue = attrLiteral.Token.ValueText;
                    if (literalValue != "null")
                    {
                        throw new Exception($"Union parameters must ne types or null at line {attrLiteral.GetLocation().GetLineSpan().StartLinePosition.Line}");
                    }
                    
                    return "null";

                case TypeOfExpressionSyntax attrTypeOf:
                    var typeSyntax = attrTypeOf.Type as IdentifierNameSyntax;
                    if (typeSyntax != null)
                    {
                        return typeSyntax.Identifier.ValueText;
                    }
                    else
                    {
                        var attrPredefined = attrTypeOf.Type as PredefinedTypeSyntax;
                        if ( attrPredefined == null )
                        {
                            throw new Exception($"Cant process identifier in AvroField attribute {args.Expression} at line {attrTypeOf.GetLocation().GetLineSpan().StartLinePosition.Line}");
                        }
                        return attrPredefined.Keyword.ValueText;
                    }

                default:
                    throw new Exception($"Unknown attribute expression type {args.Expression} at line {args.Expression.GetLocation().GetLineSpan().StartLinePosition.Line}");
            }
        }

        private static string GetDefaultValueFromAttribute(AttributeSyntax attr)
        {
            foreach (var args in attr.ArgumentList.Arguments)
            {
                switch (args.Expression)
                {
                    case LiteralExpressionSyntax attrLiteral:
                        return attrLiteral.Token.ValueText;
                    default:
                        throw new Exception($"Unknown attribute expression type {args.Expression} at line {args.Expression.GetLocation().GetLineSpan().StartLinePosition.Line}");
                }
            }
            return null;
        }

#pragma warning disable SA1008 // Doesnt deal with tuples
        private static (string, string) GetAvroFieldFromAttribute(AttributeSyntax attr)
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
    }
}
