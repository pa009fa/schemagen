using System;
using System.Collections.Generic;

namespace Avro.SchemaGen
{
    public class DAG<I, T>
    {
        private static object _sync = new object();

        private Dictionary<I, Node> _allNodes = new Dictionary<I, Node>();
        private Dictionary<I, Node> _rootNodes = new Dictionary<I, Node>();

        public class Node
        {
            private List<Node> _references = new List<Node>();

            public I Id { get; set; }

            public T Value { get; set; }

            public List<Node> Edges { get => _references; }

            public void AddEdge(Node node)
            {
                _references.Add(node);
            }
        }

        public Dictionary<I, Node> AllNodes { get => _allNodes; }

        public Dictionary<I, Node> RootNodes { get => _rootNodes; }

        public void AddOrUpdateNode(I id, T value)
        {
            lock (_sync)
            {
                if (AllNodes.ContainsKey(id))
                {
                    Node node;
                    AllNodes.TryGetValue(id, out node);
                    node.Value = value;
                }
                else
                {
                    var node = new Node() { Id = id, Value = value };
                    AllNodes.Add(id, node);
                    RootNodes.Add(id, node);
                }
            }
        }

        public void AddEdge(I from, I to)
        {
            lock (_sync)
            {
                if (!AllNodes.ContainsKey(from))
                {
                    throw new Exception($"ClassNode {from} not found");
                }

                if (!AllNodes.ContainsKey(to))
                {
                    AddOrUpdateNode(to, default(T));
                }

                if (RootNodes.ContainsKey(to))
                {
                    RootNodes.Remove(to);
                }

                Node fromNode;
                AllNodes.TryGetValue(from, out fromNode);
                Node toNode;
                AllNodes.TryGetValue(to, out toNode);
                fromNode.AddEdge(toNode);
            }
        }
    }
}
