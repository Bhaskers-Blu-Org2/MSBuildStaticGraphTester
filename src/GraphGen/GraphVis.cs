﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GraphVizWrapper;
using GraphVizWrapper.Commands;
using GraphVizWrapper.Queries;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;

namespace GraphGen
{
    public class GraphVis
    {
        public static string Create(IEnumerable<ProjectGraphNode> projects)
        {
            return Create(projects, new GraphVisOptions());
        }

        public static string Create(ProjectGraph graph)
        {
            return Create(graph, new GraphVisOptions());
        }

        public static string Create(ProjectGraph graph, GraphVisOptions options)
        {
            // I don't really remember why I did the hash thing. I think I was concerned with duplicate nodes?
            var projects = new ConcurrentDictionary<string, ProjectGraphNode>();

            var selectedProjects = graph.ProjectNodes.Where(p => !p.ProjectInstance.FullPath.Contains("dirs.proj"));

            return Create(selectedProjects, options);
        }

        public static string Create(IEnumerable<ProjectGraphNode> graphNodes, GraphVisOptions options)
        {
            var graphNodesSet = graphNodes.ToHashSet();
            var seen = new HashSet<ProjectGraphNode>();

            var sb = new StringBuilder();
            var edges = new StringBuilder();
            //var nodes = new StringBuilder();
            var clusters = new StringBuilder();

            foreach (var group in graphNodesSet
                .GroupBy(n => n.ProjectInstance.FullPath, (p, plist) => new { ProjectGroupName = p, Projects = plist}))
            {
                GraphVisCluster cluster = new GraphVisCluster(group.ProjectGroupName);

                foreach (var node in group.Projects)
                {
                    var graphNode = new GraphVisNode(node);
                    cluster.AddNode(graphNode);
                    
                    if (seen.Contains(node)) continue;
                    seen.Add(node);

                    // skip references not in the set of input nodes, in case a subgraph was given
                    foreach (var subNode in node.ProjectReferences.Where(r => graphNodesSet.Contains(r)))
                    {
                        var subGraphVisNode = new GraphVisNode(subNode);
                        var edgeString = new GraphVisEdge(graphNode, subGraphVisNode);

                        edges.AppendLine(edgeString.Create());

                        //if (!seen.Contains(node))
                        //    nodes.AppendLine(subGraphVisNode.Create());
                    }
                }

                clusters.AppendLine(cluster.Create());
            }

            sb.AppendLine("digraph prof {");
            sb.AppendLine("  ratio = fill;");
            sb.AppendLine($"  nodesep = {options.NodeSep};");
            sb.AppendLine($"  ranksep = {options.RankSep};");
            sb.AppendLine("  node [style=filled];");
            sb.Append(clusters);
            sb.Append(edges);
            sb.AppendLine("}");
            GraphVisNode._count = 1;
            return sb.ToString();
        }

        public static void Save(string graphText, string outFile)
        {
            var outFileInfo = new FileInfo(outFile);

            // These three instances can be injected via the IGetStartProcessQuery, 
            //                                               IGetProcessStartInfoQuery and 
            //                                               IRegisterLayoutPluginCommand interfaces

            var getStartProcessQuery = new GetStartProcessQuery();
            var getProcessStartInfoQuery = new GetProcessStartInfoQuery();
            var registerLayoutPluginCommand = new RegisterLayoutPluginCommand(getProcessStartInfoQuery, getStartProcessQuery);

            // GraphGeneration can be injected via the IGraphGeneration interface

            var wrapper = new GraphGeneration(getStartProcessQuery,
                getProcessStartInfoQuery,
                registerLayoutPluginCommand);

            Enums.GraphReturnType saveType;
            switch (outFileInfo.Extension)
            {
                case ".pdf":
                    saveType = Enums.GraphReturnType.Pdf;
                    break;
                case ".jpg":
                    saveType = Enums.GraphReturnType.Jpg;
                    break;
                case ".png":
                    saveType = Enums.GraphReturnType.Png;
                    break;
                default:
                    throw new Exception($"Unknown extension: {outFileInfo.Extension}");

            }

            var currentDirectory = Directory.GetCurrentDirectory();

            byte[] output;

            try
            {
                Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                output = wrapper.GenerateGraph(graphText, saveType);
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDirectory);
            }

            File.WriteAllBytes(outFile, output);

            Console.WriteLine();
            Console.WriteLine($"{output.Length} bytes written to {outFile}.");
        }

        private const char ItemSeparatorCharacter = '\u2028';

        private static string HashGlobalProps(IDictionary<string, string> globalProperties)
        {
            using (var sha1 = SHA1.Create())
            {
                var stringBuilder = new StringBuilder();
                foreach (var item in globalProperties)
                {
                    stringBuilder.Append(item.Key);
                    stringBuilder.Append(ItemSeparatorCharacter);
                    stringBuilder.Append(item.Value);
                }

                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(stringBuilder.ToString()));

                stringBuilder.Clear();

                foreach (var b in hash)
                {
                    stringBuilder.Append(b.ToString("x2"));
                }

                return stringBuilder.ToString();
            }
        }
    }

    public class GraphVisOptions
    {
        public double RankSep { get; set; } = 3.0;
        public double NodeSep { get; set; } = .1;
    }
}