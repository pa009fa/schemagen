using System.Collections.Generic;

namespace Avro.SchemaGen
{
    public class SchemaTypeInfo
    {
        public enum SyntaxType
        {
            primitive,
            complex,
            array,
            generic,
            nullable
        }
        public SyntaxType Syntax { get; set; }

        public string TypeName { get; set; }
        public List<SchemaTypeInfo> GenericParameters { get; set; }
    }
}
