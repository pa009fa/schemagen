using System;
using System.Collections.Generic;

namespace Avro.SchemaGen
{
    public class ReferenceGraph<I, T>
    {
        private static object _sync = new object();

        private Dictionary<I, ClassNode> _allNodes = new Dictionary<I, ClassNode>();
        private Dictionary<I, ClassNode> _rootNodes = new Dictionary<I, ClassNode>();

        public class ClassNode
        {
            private List<ClassNode> _references = new List<ClassNode>();

            public I Id { get; set; }

            public T Value { get; set; }

            public List<ClassNode> References { get => _references; }

            public void AddReference(ClassNode node)
            {
                _references.Add(node);
            }
        }

        public Dictionary<I, ClassNode> AllNodes { get => _allNodes; }

        public Dictionary<I, ClassNode> RootNodes { get => _rootNodes; }

        public void AddOrUpdateClass(I id, T value)
        {
            lock (_sync)
            {
                if (AllNodes.ContainsKey(id))
                {
                    ClassNode node;
                    AllNodes.TryGetValue(id, out node);
                    node.Value = value;
                }
                else
                {
                    var node = new ClassNode() { Id = id, Value = value };
                    AllNodes.Add(id, node);
                    RootNodes.Add(id, node);
                }
            }
        }

        public void AddReference(I from, I to)
        {
            lock (_sync)
            {
                if (!AllNodes.ContainsKey(from))
                {
                    throw new Exception($"ClassNode {from} not found");
                }

                if (!AllNodes.ContainsKey(to))
                {
                    AddOrUpdateClass(to, default(T));
                }

                if (RootNodes.ContainsKey(to))
                {
                    RootNodes.Remove(to);
                }

                ClassNode fromNode;
                AllNodes.TryGetValue(from, out fromNode);
                ClassNode toNode;
                AllNodes.TryGetValue(to, out toNode);
                fromNode.AddReference(toNode);
            }
        }
    }
}
