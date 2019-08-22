using System;
using System.Collections.Generic;

namespace Avro.SchemaGen
{
    public class ReferenceGraphDepthFirstTraverser<I, T>
    {
        public ReferenceGraph<I, T> Graph { get; set; }

        public ReferenceGraphDepthFirstTraverser(ReferenceGraph<I, T> graph)
        {
            Graph = graph;
        }

        private void Visit(ReferenceGraph<I, T>.ClassNode node, List<I> visited, Action<I, T> visit)
        {
            visited.Add(node.Id);
            foreach (var n in node.References)
            {
                if (!visited.Contains(n.Id))
                {
                    Visit(n, visited, visit);
                }
            }

            if (visit != null)
            {
                visit(node.Id, node.Value);
            }
        }

        public void Visit(Action<I, T> visit)
        {
            var visited = new List<I>();
            foreach (var rn in Graph.RootNodes)
            {
                Visit(rn.Value, visited, visit);
            }
        }
    }
}
