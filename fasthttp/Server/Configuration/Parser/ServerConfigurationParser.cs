﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastHTTP.Server.Configuration.Parser
{
    /// <summary>
    /// A class to parse configuration files generated by fasthttp
    /// </summary>
    public class ServerConfigurationParser
    {
        private string configPath;
        private string currentSName = "";
        private List<string> currentSectionBody = new List<string>();
        private int blockDepth = 0;
        private ConfigReadMode currentReadMode = ConfigReadMode.Normal;
        private int lineNumber = 0;

        public Dictionary<string, ConfigSection> DefinedSections { get; set; } = new Dictionary<string, ConfigSection>();
        public Dictionary<string, ConfigVariable> DefinedConstants { get; set; } = new Dictionary<string, ConfigVariable>();

        /// <summary>
        /// Creates a parser to parse configuration path
        /// </summary>
        /// <param name="configPath">The file to parse</param>
        public ServerConfigurationParser(string configPath)
        {
            this.configPath = configPath;
            DefinedSections["__global__"] = new ConfigSection();
            DefinedConstants["FHTTPVERSION"] = new ConfigVariable() { DataType = ConfigVariableDataType.Integer, Name = "FHTTPVERSION", Value = 1 };
        }
        
        /// <summary>
        /// Defines a constant
        /// </summary>
        /// <param name="name">The name of the constant to define</param>
        /// <param name="dataType">The data type to use</param>
        /// <param name="value">The value of the constant</param>
        public void DefineConstant(string name, ConfigVariableDataType dataType, object value, int trackingLineNumber = -1)
        {
            if (DefinedConstants.ContainsKey(name)) throw new ConstantAlreadyDefinedException(name, trackingLineNumber);
            DefinedConstants[name] = new ConfigVariable()
            {
                Name = name,
                DataType = dataType,
                Value = value
            };
        }

        /// <summary>
        /// Parses the current file
        /// </summary>
        public void Parse()
        {
            _Parse(File.ReadAllLines(configPath));
        }

        /// <summary>
        /// Parses the current file with a context
        /// </summary>
        /// <param name="context">The context to use when parsing</param>
        public void Parse(string context)
        {
            _Parse(File.ReadAllLines(configPath), context);
        }

        /// <summary>
        /// Parse a variable from string
        /// </summary>
        /// <param name="str">The variable string to parse</param>
        /// <returns>A variable constructed from the specified string</returns>
        private ConfigVariable ParseVar(string str)
        {
            string variableName = str.Substring(0, str.IndexOf(' '));
            string rightHandValue = str.Substring(str.IndexOf(' ') + 1).Trim();
            ConfigVariableDataType dataType = ConfigVariableDataType.Integer; // Assume integer at first
            bool escapeMode = false; //Are we in escape char mode?
            bool literalTerminated = false;
            bool literalTypeFound = false;
            string resultantString = "";
            int resultantInt = 0;
            List<string> resultantList = new List<string>();
            for(int i=0;i<rightHandValue.Length;i++)
            {
                if (rightHandValue[i] == '\\')
                {
                    escapeMode = true;
                    continue;
                }
                else if ((rightHandValue[i] == '"') && (!escapeMode)) //Found a string quote
                {
                    if(!literalTypeFound)
                    {
                        literalTypeFound = true;
                        dataType = ConfigVariableDataType.String;
                        continue;
                    }
                    //This is a string terminator
                    literalTerminated = true;
                    break; //Exit the loop
                }
                switch (dataType)
                {
                    case ConfigVariableDataType.String:
                        if(escapeMode)
                        {
                            switch(rightHandValue[i])
                            {
                                case 't':
                                    resultantString += '\t';
                                    break;
                                case 'n':
                                    resultantString += '\n';
                                    break;
                                case 'a':
                                    resultantString += '\a';
                                    break;
                                case '"':
                                    resultantString += '"';
                                    break;
                            }
                        }
                        else resultantString += rightHandValue[i];
                        break;
                    case ConfigVariableDataType.Integer:
                        if (rightHandValue[i] == ' ') break; //Reached the end of literal
                        resultantString += rightHandValue[i]; //String like operation
                        break;
                }
                if (escapeMode)
                {
                    escapeMode = false;
                    continue;
                }
            }
            if ((dataType == ConfigVariableDataType.String) && (!literalTerminated)) throw new StringLiteralNotTerminatedException(variableName, lineNumber);
            if (dataType == ConfigVariableDataType.Integer) resultantInt = int.Parse(resultantString.Trim());
            return new ConfigVariable { DataType = dataType, Name = variableName, Value = new Func<object>(()=> {
                if (dataType == ConfigVariableDataType.Integer) return resultantInt;
                else return resultantString;
            })() }; //TODO optimize code here pls
        }

        /// <summary>
        /// Parse and evaluate text
        /// </summary>
        /// <param name="data">The lines to parse</param>
        /// <param name="ctx">The context to run under (global is __global__)</param>
        private void _Parse(string[] data, string ctx = "__global__")
        {
            foreach(var rl in data)
            {
                lineNumber++;
                var ln = rl.Trim();
                if (ln == "") continue;
                else if (ln.StartsWith("//")) continue;
                foreach(var c in DefinedConstants)
                    if(currentReadMode != ConfigReadMode.MultiLineComment)
                        ln = ln.Replace("$" + c.Key + "$", c.Value.Value.ToString());
                foreach (var c in DefinedSections[ctx].Variables)
                    if (currentReadMode != ConfigReadMode.MultiLineComment)
                        ln = ln.Replace("$" + c.Key + "$", c.Value.Value.ToString());
                switch (currentReadMode)
                {
                    default:
                        //What read mode is this?!
                        break;
                    case ConfigReadMode.Normal:
                        if (ln.StartsWith("/*"))
                        {
                            currentReadMode = ConfigReadMode.MultiLineComment;
                            continue;
                        } else if(ln.StartsWith("const "))
                        {
                            // DEBUG only Console.WriteLine("Found const, lets parse it");
                            var v = ParseVar(ln.Substring(6).Trim());
                            DefineConstant(v.Name, v.DataType, v.Value, lineNumber);
                        } else if(ln.StartsWith("section "))
                        {
                            currentReadMode = ConfigReadMode.SectionDef;
                            currentSName = ln.Split(' ')[1].Trim();
                            blockDepth = 1;
                            continue;
                        } else if(ln.StartsWith("include "))
                        {
                            if (!ln.Contains("\"")) continue;
                            string fileName = ln.Split('"')[1].Trim();
                            if (!Path.IsPathRooted(fileName))
                                fileName = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)), fileName);
                            if (!File.Exists(fileName)) throw new IncludeNotFoundException(fileName, lineNumber);
                            var p = new ServerConfigurationParser(fileName);
                            p.DefinedConstants = DefinedConstants;
                            p.DefinedSections = DefinedSections;
                            p.Parse(ctx);
                        } else if(ln.Contains(" ")) //Probably a property
                        {
                            ConfigVariable var = ParseVar(ln.Trim());
                            DefinedSections[ctx].Variables[var.Name] = var;
                        }
                        break;
                    case ConfigReadMode.SectionDef:
                        if (ln.EndsWith("{")) blockDepth++;
                        else if (ln.EndsWith("}")) blockDepth--;
                        if(blockDepth == 0) //Finished searching section
                        {
                            var ctxedName = ctx + "." + currentSName;
                            DefinedSections[ctxedName] = new ConfigSection();
                            currentReadMode = ConfigReadMode.Normal;
                            var p = new ServerConfigurationParser(null);
                            p.DefinedConstants = DefinedConstants;
                            p.DefinedSections = DefinedSections;
                            p.lineNumber = lineNumber;
                            p.configPath = configPath;
                            p._Parse(currentSectionBody.ToArray(), ctxedName);
                            currentSName = "";
                            currentSectionBody.Clear();
                            continue;
                        }
                        currentSectionBody.Add(ln);
                        break;
                    case ConfigReadMode.MultiLineComment:
                        if (ln.EndsWith("*/"))
                        {
                            currentReadMode = ConfigReadMode.Normal;
                            continue;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Displays a results table for the parser
        /// </summary>
        /// <returns></returns>
        public void DisplayResultsTable()
        {
            StringBuilder contents = new StringBuilder();
            contents.AppendLine("Configuration Parser Results Table\n----------------------------------\n");
            contents.AppendLine("[Defined constants]\n");
            DefinedConstants.Keys.All((s) =>
            {
                contents.AppendLine(string.Format("  Constant Name: {0}, Constant Value: {1}", s, DefinedConstants[s].Value));
                return true;
            });
            contents.AppendLine("\n[Defined Sections]\n");
            DefinedSections.Keys.All((s) =>
            {
                contents.AppendLine(string.Format("  Section Name: {0}", s));
                DefinedSections[s].Variables.All((v) =>
                {
                    contents.AppendLine(string.Format("    {0} = {1}", v.Key, v.Value.Value));
                    return true;
                });
                return true;
            });
            Console.WriteLine(contents);
        }
    }
}