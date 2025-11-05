

using System;
using System.Threading.Tasks;
using YamlDotNet.Core.Tokens;
using YamlDotNet.RepresentationModel;

namespace tool
{
    internal class Program
    {
        public const int GAME_OBJECT_ID = 1, MAX_DEGREE_OF_PARALLELISM = 6;
        static void Main( string[] args)
        {
            Console.WriteLine(Directory.GetCurrentDirectory());
            if (args == null || args.Length<2) { args = new string[2];
            args[0] = @"..\..\..\..\TestCase01";
            args[1] = @"..\..\..\..\Dump";
            }
             
            Analize(args[0], args[1]);
        }

        private static void Analize(string inputPath, string outputPath)
        {
            Task t1 = Task.Run(() => LocateAndDumpAllScenes(inputPath + @"\Assets", outputPath));
            Task t2 = Task.Run(() => LocateUnusedScripts(inputPath + @"\Assets", outputPath));
            Task[] tasks = { t1, t2 };
            Task.WaitAll(tasks);
        }

        //change void into task later
        private static async void LocateUnusedScripts(string v, string outputPath)
        {
            return;
        }

        private static async Task LocateAndDumpAllScenes(string assetsPath, string outputPath)
        {
            if (!Directory.Exists(assetsPath)) throw new Exception("Assets folder could not be found.");
            string[] sceneFilePaths = Directory.GetFiles(assetsPath,"*.unity",SearchOption.AllDirectories);

            if (sceneFilePaths.Length == 0) throw new Exception("0 Scenes found.");
            else
            {
                ParallelOptions parallelOptions = new()
                {
                    MaxDegreeOfParallelism = MAX_DEGREE_OF_PARALLELISM
                };
                await Parallel.ForEachAsync<string>(sceneFilePaths, parallelOptions, async (sceneFilePath, ct) => { await DumpScene(sceneFilePath, outputPath); });
            }
        }
        private static async Task DumpScene(string inputFilePath, string outputPath)
        {
            try {

           StreamWriter streamWriter = File.CreateText(Path.Combine(outputPath, Path.GetFileName(inputFilePath)+".dump"));
                streamWriter.AutoFlush = true;
            StringReader input = new StringReader(File.ReadAllText(inputFilePath));
            YamlStream yaml = new YamlStream();
            yaml.Load(input);
            string dumpText = "";
            RecursivePrintTree(0,"0",inputFilePath,yaml.Documents.ToList(), LocateGameObjects(inputFilePath), ref dumpText);
            await streamWriter.WriteAsync(dumpText);
            }catch(Exception e)
            {
                Console.WriteLine($"ERROR: Could not dump {Path.GetFileName(inputFilePath)}. " + e.Message);
            }
        }

        private static void RecursivePrintTree(int depth, string fatherId, string filePath, List<YamlDocument> yamlDocuments, Dictionary<string, string> gameObjectDictionary, ref string dumpText)
        {

            for (int i = 0; i < yamlDocuments.Count; i++)
            {
                
                YamlMappingNode rootNode = (YamlMappingNode)yamlDocuments[i].RootNode;

                
                if (((YamlScalarNode)rootNode.Children[0].Key).Value == "Transform")
                {
                     
                    YamlMappingNode transformNode = (YamlMappingNode)rootNode.Children[0].Value;

                     
                    if(transformNode.Children.TryGetValue(new YamlScalarNode("m_Father"), out YamlNode fatherNode) && ((YamlMappingNode)fatherNode).Children.TryGetValue(new YamlScalarNode("fileID"), out YamlNode fatherIdNode) && fatherId.Equals(((YamlScalarNode)fatherIdNode).Value) && transformNode.Children.TryGetValue(new YamlScalarNode("m_GameObject"), out YamlNode gameObjectNode) && ((YamlMappingNode)gameObjectNode).Children.TryGetValue(new YamlScalarNode("fileID"), out YamlNode idNode) && gameObjectDictionary.TryGetValue(((YamlScalarNode)idNode).Value, out string name))
                    {
                        
                        dumpText= dumpText + (new string('-', 2 * depth)+name) + "\n";
                         
                        if (transformNode.Children.TryGetValue(new YamlScalarNode("m_Children"), out YamlNode childrenNode) && childrenNode is YamlSequenceNode && ((YamlSequenceNode)childrenNode).Children.Count > 0)
                        {
                            
                           
                            RecursivePrintTree(depth+1, yamlDocuments[i].RootNode.Anchor.Value, filePath, yamlDocuments, gameObjectDictionary, ref dumpText);
                        }


                    }
                    
                }

            }
        }

        private static Dictionary<string, string> LocateGameObjects(string filePath)
        {
              

            string[] yaml = File.ReadAllLines(filePath);
            Dictionary<string, string> gameObjectDictionary = new();
            for (int i = 0; i < yaml.Length; i++) {

                if (yaml[i].TrimStart().StartsWith($"--- !u!{GAME_OBJECT_ID} &"))
                {
                    string gameObjectId = yaml[i].Split('&')[1].TrimEnd();
                    for(;i<yaml.Length;i++)
                    {
                        string property = yaml[i].TrimStart();
                        if (property.StartsWith("m_Name:"))
                        {
                            string gameObjectName = yaml[i].Split("m_Name:")[1].TrimStart();
                            gameObjectDictionary.Add(gameObjectId, gameObjectName);
                            
                            break;
                        }
                        }
                }
            
            }
            return gameObjectDictionary;
             
        }
    }
}
