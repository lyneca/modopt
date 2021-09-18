using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using ThunderRoad;

namespace ModOpt {
    public class Runner : LevelModule {
        List<ModData> mods = new List<ModData>();
        char descriptionDelimiter = '|';

        public override IEnumerator OnLoadCoroutine(Level level) {
            var modDirectories = Directory.GetDirectories(FileManager.aaModPath);
            var mods = new List<ModData>();
            foreach (var path in modDirectories) {
                if (path.Split('\\').Last().StartsWith("_")) continue;
                var mod = ImportMod(path);
                if (mod.modules.Count > 0) mods.Add(mod);
            }

            foreach (var mod in mods) {
                foreach (var module in mod.modules) {
                    var assemblyPath = Path.Combine(mod.path, module.moduleAssembly + ".dll");
                    Debug.Log("READING ASSEMBLY: " + assemblyPath.ToString());
                    Assembly a = Assembly.LoadFile(assemblyPath);
                    Type type = a.GetType(module.moduleClass);
                    if (!type.IsPublic) continue;

                    Dictionary<string, object> defaultValues = new Dictionary<string, object>();
                    try {
                        object instance = Activator.CreateInstance(type);
                        defaultValues = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(instance));
                    }
                    catch (Exception e) {
                        Console.WriteLine("ERROR: " + e.Message + "\n" + e.InnerException);
                    }

                    var description = type.GetCustomAttribute<DescriptionAttribute>()?.Description;
                    if (!string.IsNullOrEmpty(description)) {
                        if (description.Contains(descriptionDelimiter)) {
                            var temp = description.Split(descriptionDelimiter);
                            module.description = temp[0];
                            module.title = temp[1];
                        } else module.description = description;
                    }


                    try {
                        using (StreamReader json = File.OpenText(module.filePath))
                        using (JsonTextReader reader = new JsonTextReader(json)) {
                            var o = (JObject)JToken.ReadFrom(reader);

                            var properties = ((TypeInfo)type).DeclaredProperties;
                            foreach (var prop in properties) {
                                var setting = new ModSetting {
                                    category = prop.GetCustomAttribute<CategoryAttribute>()?.Category,
                                    defaultValue = prop.GetCustomAttribute<DefaultValueAttribute>()?.Value,
                                    name = Regex.Replace(prop.Name, @"((?<=\p{Ll})\p{Lu})|((?!\A)\p{Lu}(?>\p{Ll}))", " $0"),
                                    getter = prop.GetGetMethod(),
                                    setter = prop.GetSetMethod()
                                };
                                var settingDescription = prop.GetCustomAttribute<DescriptionAttribute>()?.Description;
                                if (!string.IsNullOrEmpty(settingDescription)) {
                                    if (settingDescription.Contains(descriptionDelimiter)) {
                                        var temp = settingDescription.Split(descriptionDelimiter);
                                        setting.description = temp[0];
                                        setting.name = temp[1];
                                    } else setting.description = settingDescription;
                                }

                                var nonStaticPropName = char.ToLower(prop.Name[0]) + prop.Name.Substring(1);

                                if (setting.defaultValue == null) {
                                    if (defaultValues.TryGetValue(nonStaticPropName, out object val)) {
                                        setting.defaultValue = val;
                                    }
                                }

                                setting.value = Convert.ChangeType(o.SelectToken(module.tokenPath + "." + nonStaticPropName), prop.PropertyType);

                                module.settings.Add(setting);
                            }
                        }
                    }
                    catch { }
                }
            }

            Debug.Log("LOADED " + mods.Count + " MOD(S)");

            yield break;
        }

        static ModData ImportMod(string path) {
            var files = Directory.GetFiles(path, "*.json");
            var mod = new ModData {
                path = path
            };
            foreach (var file in files) {
                var fileName = file.Split('\\').Last();
                if (fileName.StartsWith("catalog_")) continue;

                try {
                    using (StreamReader json = File.OpenText(file))
                    using (JsonTextReader reader = new JsonTextReader(json)) {
                        var o = (JObject)JToken.ReadFrom(reader);

                        if (fileName.ToLower() == "manifest.json") {
                            mod.name = (string)o.SelectToken("Name");
                            continue;
                        }

                        var modules = ParseJsonFile(o);
                        foreach (var module in modules) {
                            module.filePath = file;
                            mod.modules.Add(module);
                        }
                    }
                }
                catch (UnauthorizedAccessException e) {
                    Console.WriteLine("ERROR: " + e.Message);
                }
            }
            return mod;
        }

        static List<ModModule> ParseJsonFile(JObject o) {
            var modModules = new List<ModModule>();

            if ((string)o.SelectToken("$type") == "ThunderRoad.LevelData, Assembly-CSharp" && (string)o.SelectToken("id") == "Master") {
                var modes = o.SelectToken("modes");
                foreach (var mode in modes) {
                    if ((string)mode.SelectToken("name") == "Default") {
                        var modules = mode.SelectToken("modules");
                        foreach (var module in modules) {
                            if (module.Count() > 1) {
                                var typeData = ((string)module.SelectToken("$type")).Split(',');
                                var m = new ModModule {
                                    moduleAssembly = typeData[1].Trim(),
                                    moduleClass = typeData[0].Trim(),
                                    tokenPath = module.Path
                                };
                                modModules.Add(m);
                            }
                        }
                    }
                }
            }
            return modModules;
        }
    }

    [Serializable]
    class ModData {
        public string name;
        public string path;
        public List<ModModule> modules = new List<ModModule>();
    }

    [Serializable]
    class ModModule {
        public string description;
        public string title;
        public List<ModSetting> settings = new List<ModSetting>();

        public string filePath;
        public string moduleAssembly;
        public string moduleClass;
        public string tokenPath;
    }

    [Serializable]
    class ModSetting {
        public string category;
        public string description;
        public string name;
        public object value;
        public object defaultValue;
        public MethodInfo getter;
        public MethodInfo setter;
    }
}
