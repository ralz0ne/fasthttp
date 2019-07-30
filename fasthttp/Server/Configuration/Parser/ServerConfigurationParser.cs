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
        private string currentSection = "__global__"; // The global scope is pre defined as __global__
        private string currentSName = "";
        private List<string> currentSectionBody = new List<string>();
        private int blockDepth = 0;
        private ConfigReadMode currentReadMode = ConfigReadMode.Normal;

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
        /// Parses the current file
        /// </summary>
        public void Parse()
        {
            _Parse(File.ReadAllLines(configPath));
        }

        private ConfigVariable ParseVar(string str)
        {
            string variableName = str.Substring(0, str.IndexOf(' '));
            string rightHandValue = str.Substring(str.IndexOf(' ') + 1).Trim();
            ConfigVariableDataType dataType = ConfigVariableDataType.Integer; // Assume integer at first
            bool escapeMode = false; //Are we in escape char mode?
            bool literalTerminated = false;
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
                else if (escapeMode)
                {
                    escapeMode = false;
                    continue;
                }
                else if (rightHandValue[i] == '"') //Found a string quote
                {
                    if(!literalTerminated)
                    {
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
                        resultantString += rightHandValue[i];
                        break;
                    case ConfigVariableDataType.Integer:
                        resultantString += rightHandValue[i]; //String like operation
                        break;
                }
            }
            if (dataType == ConfigVariableDataType.Integer) resultantInt = int.Parse(resultantString.Trim());
            return new ConfigVariable { DataType = dataType, Name = variableName, Value = new Func<object>(()=> {
                if (dataType == ConfigVariableDataType.Integer) return resultantInt;
                else return resultantString;
            })() }; //TODO optimize code here pls
        }

        private void _Parse(string[] data, string ctx = "__global__")
        {
            foreach(var rl in data)
            {
                var ln = rl.Trim();
                if (ln == "") continue;
                else if (ln.StartsWith("//")) continue;
                foreach(var c in DefinedConstants)
                    if(currentReadMode != ConfigReadMode.MultiLineComment)
                        ln = ln.Replace("$" + c.Key + "$", c.Value.Value.ToString());
                switch(currentReadMode)
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
                            Console.WriteLine("Found const, lets parse it");
                            var v = ParseVar(ln.Substring(6).Trim());
                            DefinedConstants[v.Name] = v;
                        } else if(ln.StartsWith("section "))
                        {
                            currentReadMode = ConfigReadMode.SectionDef;
                            currentSName = ln.Split(' ')[1].Trim();
                            blockDepth = 1;
                            continue;
                        }
                        break;
                    case ConfigReadMode.SectionDef:
                        if(ln.EndsWith("{"))
                        {
                            blockDepth++;
                            continue;
                        } else if(ln.StartsWith("}"))
                        {
                            blockDepth--;
                            if (blockDepth == 0)
                            {
                                foreach(var l2n in currentSectionBody)
                                {
                                    Console.WriteLine("SECTION BODY {0}: {1}", ctx += "." + currentSName, l2n);
                                }
                                DefinedSections[ctx += "." + currentSName] = new ConfigSection();
                                //Execute results
                                _Parse(currentSectionBody.ToArray(), ctx += "." + currentSName);
                                //We are finished defining and executing
                                currentSName = "";
                                currentReadMode = ConfigReadMode.Normal;
                                currentSectionBody = new List<string>();
                                continue;
                            }
                            else continue;
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
    }
}
