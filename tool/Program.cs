

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Net.WebSockets;
using System.Text;
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
            // Console.WriteLine(Directory.GetCurrentDirectory());
            //ConfirmProperty(@"..\..\..\..\ScriptParseTesting\ScriptTest.cs", @"..\..\..\..\ScriptParseTesting\ScriptTest.cs", "ScriptTest");
            if (args == null || args.Length<2) { args = new string[2];
            args[0] = @"..\..\..\..\TestCase02";
            args[1] = @"..\..\..\..\Dump2";
            }

            try { Analize(args[0], args[1]); } catch (Exception e) { Console.WriteLine(e.Message); }
        }

        private static void Analize(string inputPath, string outputPath)
        {
            string[] sceneFilePaths, scriptMetaFilePaths;
            if (!Directory.Exists(inputPath + @"\Assets")) throw new Exception("Assets folder could not be found.");

            Task<string[]> t1 = Task<string[]>.Run(() => Directory.GetFiles(inputPath + @"\Assets", "*.unity", SearchOption.AllDirectories));
            Task<string[]> t2 = Task<string[]>.Run(() => Directory.GetFiles(inputPath + @"\Assets", "*.cs.meta", SearchOption.AllDirectories));
            Task[] dataGatheringTasks = { t1, t2 };
            Task.WaitAll(dataGatheringTasks);

            sceneFilePaths = t1.Result;
            scriptMetaFilePaths = t2.Result;

            if (sceneFilePaths.Length == 0) throw new Exception("0 Scenes found.");
            if (scriptMetaFilePaths.Length == 0) throw new Exception("0 Script meta files found.");

            Task t3 = Task.Run(() => DumpAllScenes(sceneFilePaths, outputPath));
            Task t4 = Task.Run(() => DumpUnusedScripts(scriptMetaFilePaths, sceneFilePaths, outputPath));
            Task[] dumpingTasks = { t3, t4 };
            Task.WaitAll(dumpingTasks);
        }

         
        private static void DumpUnusedScripts(string[] scriptMetaFilePaths, string[] sceneFilePaths, string outputPath)
        {   
            //first value is guid, second is path
            Dictionary<string, string> guidDictionary = GenerateGuidDictionary(scriptMetaFilePaths);

            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = MAX_DEGREE_OF_PARALLELISM
            };
            Parallel.ForEach<string>(scriptMetaFilePaths, parallelOptions, (scriptMetaFilePath, ct) => { ScanIfScriptIsUnused(scriptMetaFilePath, sceneFilePaths ,outputPath,guidDictionary); });
        }

        private static void ScanIfScriptIsUnused(string scriptMetaFilePath, string[] sceneFilePaths ,string outputPath, Dictionary<string, string> guidDictionary)
        {
            
            
            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = MAX_DEGREE_OF_PARALLELISM
            };
            bool isUsed = false;
            using CancellationTokenSource cts = new();
            Parallel.ForEach<string>(sceneFilePaths, parallelOptions, (sceneFilePath, ct) => { if (ScriptIsUsedInsideScene( scriptMetaFilePath, sceneFilePath, guidDictionary)) { ct.Stop(); isUsed = true; }  });
            if (!isUsed)Console.WriteLine(scriptMetaFilePath);


        }

        private static bool ScriptIsUsedInsideScene(string scriptMetaFilePath,string sceneFilePath, Dictionary<string, string> guidDictionary)
        {
            StringReader input = new StringReader(File.ReadAllText(sceneFilePath));
            YamlStream yaml = new();
            yaml.Load(input);

           string scriptGuid = LocateScript(scriptMetaFilePath).Item1;

            for (int i=0; i<yaml.Documents.Count();i++)
            {
                YamlMappingNode rootNode = (YamlMappingNode)yaml.Documents[i].RootNode;
                if (((YamlScalarNode)rootNode.Children[0].Key).Value == "MonoBehaviour")
                {
                    YamlMappingNode monoNode = (YamlMappingNode)rootNode.Children[0].Value;
                    string mainScriptGuid = "";
                    string mainScriptPath = "";
                    if (monoNode.Children.TryGetValue(new YamlScalarNode("m_Script"), out YamlNode scriptNode) && scriptNode is YamlMappingNode nodeMap && nodeMap.Children.TryGetValue(new YamlScalarNode("guid"), out YamlNode mainScriptGuiNode))
                    {
                        mainScriptGuid = ((YamlScalarNode)mainScriptGuiNode).Value;
                        mainScriptPath = guidDictionary[mainScriptGuid];
                    }
                    foreach (var child in monoNode.Children)
                    {
                        if (child.Value is YamlMappingNode && ((YamlMappingNode)child.Value).Children.TryGetValue(new YamlScalarNode("guid"), out YamlNode guid) && guid.ToString() == scriptGuid && (((YamlScalarNode)child.Key).Value == "m_Script" || ConfirmProperty(mainScriptPath, scriptMetaFilePath, ((YamlScalarNode)child.Key).Value))) { return true; }
                    }
                }



            }

            return false;
        }

        private static bool ConfirmProperty(string mainClassPath, string referencedClassPath, string propertyName)
        {
           
            try { 
            if(mainClassPath.Length == 0 || referencedClassPath.Length==0 || propertyName.Length==0) return false;

            if (mainClassPath.EndsWith(".meta"))
                mainClassPath = mainClassPath.Substring(0, mainClassPath.Length-5);

            if (referencedClassPath.EndsWith(".meta"))
                referencedClassPath = referencedClassPath.Substring(0, referencedClassPath.Length - 5);
            
            SyntaxTree mainClassTree = CSharpSyntaxTree.ParseText(File.ReadAllText(mainClassPath));
            var members = mainClassTree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>();

            SyntaxTree referenceClassTree = CSharpSyntaxTree.ParseText(File.ReadAllText(referencedClassPath));
            ClassDeclarationSyntax classDeclaration = referenceClassTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();

            string referencedClassName = "";
            if (classDeclaration != null) referencedClassName = classDeclaration.Identifier.Text;
            
            foreach (var member in members)
            {   //case 1: it's a public property
                if (member is PropertyDeclarationSyntax property && PropertyHasModifier(property, "public") && property.Type.ToString() == referencedClassName && propertyName == property.Identifier.Text)
                    return true;
                //case 2: it's a public field
                else if (member is FieldDeclarationSyntax field && FieldHasModifier(field,"public") && FieldHasVariable(field,propertyName) && referencedClassName == field.Declaration.Type.ToString())
                    return true; 
                //case 3: serialize field
                else if (member is FieldDeclarationSyntax privField && !FieldHasModifier(privField, "public") && HasSerializeField(privField) && FieldHasVariable(privField, propertyName) && privField.Declaration.Type.ToString() == referencedClassName) 
                    return true; 
            }
            return false;
            }catch(Exception e) { Console.WriteLine("Warning!: Could not parse " + mainClassPath + " . It will automatically be treated as unused. (Error:"+e.Message+")"); return false; }
        }

        private static bool FieldHasModifier(FieldDeclarationSyntax field, string modifierSearchCriteria)
        {
           foreach(var modifier in field.Modifiers)
            {
                if(modifier.ValueText == modifierSearchCriteria) return true;
            }
            return false;
        }

        private static bool FieldHasVariable(FieldDeclarationSyntax field, string variableSearchCriteria)
        {
            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Identifier.Text == variableSearchCriteria)
                    return true;
            }
            return false;
        }

        private static bool PropertyHasModifier(PropertyDeclarationSyntax field, string modifierSearchCriteria)
        {
            foreach (var modifier in field.Modifiers)
            {
                if (modifier.ValueText == modifierSearchCriteria) return true;
            }
            return false;
        }

        private static bool HasSerializeField(FieldDeclarationSyntax privField)
        {
            bool hasSerializeField = false;

            foreach (AttributeListSyntax attributeList in privField.AttributeLists)
            {
                foreach (AttributeSyntax attribute in attributeList.Attributes)
                {
                    string name = attribute.Name.ToString();

                    if (name == "SerializeField" ||
                        name.EndsWith(".SerializeField"))
                    {
                        hasSerializeField = true;
                        break;
                    }
                }

                if (hasSerializeField)
                    break;
            }
            return hasSerializeField;
        }

        private static Dictionary<string, string> GenerateGuidDictionary(string[] scriptMetaFilePaths)
        {
            Dictionary<string, string> guidDictionary = new();
            foreach (string scriptMetaFilePath in scriptMetaFilePaths) {
                Tuple<string, string> script = LocateScript(scriptMetaFilePath);
                guidDictionary.Add(script.Item1,script.Item2);

            }
            return guidDictionary;
        }

        private static Tuple<string, string> LocateScript(string scriptMetaFilePath)
        {
            try {
                string[] yaml = File.ReadAllLines(scriptMetaFilePath);

                foreach (string line in yaml) {
                    if (line.StartsWith("guid: ")) return new(line.Split("guid: ")[1].TrimEnd(), scriptMetaFilePath);
            }
                throw new Exception("GUID not found in yaml.");
            } catch (Exception ex) { throw new Exception("ERROR: Could not locate meta file for script "+scriptMetaFilePath+". ("+ex.Message+")"); }
        }



        private static async Task DumpAllScenes(string[] sceneFilePaths, string outputPath)
        {
            
                ParallelOptions parallelOptions = new()
                {
                    MaxDegreeOfParallelism = MAX_DEGREE_OF_PARALLELISM
                };
                await Parallel.ForEachAsync<string>(sceneFilePaths, parallelOptions, async (sceneFilePath, ct) => { await DumpScene(sceneFilePath, outputPath); });
            
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
