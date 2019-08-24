using System;
using System.Collections.Generic;

namespace Avro.SchemaGen
{
    public class DAGDepthFirstTraverser<I, T>
    {
        public DAG<I, T> Graph { get; set; }

        public DAGDepthFirstTraverser(DAG<I, T> graph)
        {
            Graph = graph;
        }

        private void Visit(DAG<I, T>.Node root, List<I> visited, Action<I, T> visit)
        {
            visited.Add(root.Id);
            foreach (var n in root.Edges)
            {
                if (!visited.Contains(n.Id))
                {
                    try
                    {
                        Visit(n, visited, visit);
                    }
                    catch
                    {
                        Console.Error.WriteLine($"node {root.Id} value {root.Value}");
                        throw;
                    }
                }
            }

            if (visit != null)
            {
                visit(root.Id, root.Value);
            }
        }

        public void Traverse(Action<I, T> visit)
        {
            var visited = new List<I>();
            foreach (var rn in Graph.RootNodes)
            {
                Visit(rn.Value, visited, visit);
            }
        }
    }
}
