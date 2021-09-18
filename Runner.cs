using IngameDebugConsole;
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
        static List<ModData> mods = new List<ModData>();
        static char descriptionDelimiter = '|';

        public override IEnumerator OnLoadCoroutine(Level level) {
            LoadAllMods();

            DebugLogConsole.AddCommand("mom_count", "", () => Debug.Log(mods.Count()));
            DebugLogConsole.AddCommand("mom_get", "", (int mod, int module, int setting) => {
                try {
                    var settingData = mods[mod].modules[module].settings[setting];
                    Debug.Log("Name: " + settingData.name + "\nValue: " + settingData.value);
                    } catch (Exception e) {
                    Debug.LogError(e.InnerException);
                }
                });
            DebugLogConsole.AddCommand("mom_set", "", (int mod, int module, int setting, string value) => {
                try {
                    var settingData = mods[mod].modules[module].settings[setting];
                    settingData.value = value;
                    Debug.Log("Set " + settingData.name + " to " + settingData.value);
                }
                catch (Exception e) {
                    Debug.LogError(e.InnerException);
                }
            });
            DebugLogConsole.AddCommand("mom_save", "", () => {
                try {
                    SaveAllMods();
                    Debug.Log("Saved!");
                }
                catch (Exception e) {
                    Debug.LogError(e.InnerException);
                }
            });

            EventManager.onReloadJson += OnReloadJson;
            Debug.Log("Mod Options Menu: Finished loading");
            yield break;
        }

        private void OnReloadJson(EventTime eventTime) {
            if (eventTime == EventTime.OnEnd) {
                LoadAllMods();
            }
        }

        static void LoadAllMods() {
            Debug.Log("Mod Options Menu: Loading all mods...");
            var modDirectories = Directory.GetDirectories(FileManager.aaModPath);
            mods = new List<ModData>();
            foreach (var path in modDirectories) {
                if (path.Split('\\').Last().StartsWith("_")) continue;
                var mod = ImportMod(path);
                if (mod.modules.Count > 0) mods.Add(mod);
            }

            foreach (var mod in mods) {
                foreach (var module in mod.modules) {
                    var assemblyPath = Path.Combine(mod.path, module.moduleAssembly + ".dll");
                    Debug.Log("Mod Options Menu: READING ASSEMBLY: " + assemblyPath.ToString());
                    Assembly a = Assembly.LoadFile(assemblyPath);
                    Type type = a.GetType(module.moduleClass);
                    if (!type.IsPublic) continue;

                    Dictionary<string, object> defaultValues = new Dictionary<string, object>();
                    try {
                        object instance = Activator.CreateInstance(type);
                        defaultValues = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(instance));
                    }
                    catch (Exception e) {
                        Console.WriteLine("Mod Options Menu: ERROR: " + e.Message + "\n" + e.InnerException);
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
                                    prop = prop
                                };
                                var settingDescription = prop.GetCustomAttribute<DescriptionAttribute>()?.Description;
                                if (!string.IsNullOrEmpty(settingDescription)) {
                                    if (settingDescription.Contains(descriptionDelimiter)) {
                                        var temp = settingDescription.Split(descriptionDelimiter);
                                        setting.description = temp[0];
                                        setting.name = temp[1];
                                    } else setting.description = settingDescription;
                                }

                                setting.jsonName = char.ToLower(prop.Name[0]) + prop.Name.Substring(1);

                                if (setting.defaultValue == null) {
                                    if (defaultValues.TryGetValue(setting.jsonName, out object val)) {
                                        setting.defaultValue = val;
                                    }
                                }

                                module.settings.Add(setting);
                            }
                        }
                    }
                    catch { }
                }
            }

            Debug.Log("Mod Options Menu: Loaded " + mods.Count + " mod(s)");
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
                    Console.WriteLine("Mod Options Menu: ERROR: " + e.Message);
                }
            }
            return mod;
        }

        static void SaveAllMods() {
            foreach (var mod in mods) {
                foreach (var module in mod.modules) {
                    try {
                        var path = module.filePath;
                        using (StreamReader json = File.OpenText(module.filePath))
                        using (JsonTextReader reader = new JsonTextReader(json)) {
                            var o = (JObject)JToken.ReadFrom(reader);
                            foreach (var setting in module.settings) {
                                var token = o.SelectToken(module.tokenPath);
                                if (token != null) {
                                    if (token[setting.jsonName] != null) {
                                        token[setting.jsonName] = JsonConvert.SerializeObject(setting.value);
                                    }
                                }
                            }
                            File.WriteAllText(module.filePath + "_mom", o.ToString());
                        }
                    }
                    catch (UnauthorizedAccessException e) {
                        Console.WriteLine("Mod Options Menu: ERROR: " + e.Message);
                    }
                }
            }
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
        public object defaultValue;
        public object value {
            get => prop.GetValue(prop);
            set => prop.SetValue(prop, Convert.ChangeType(value, prop.GetMethod.ReturnType));
        }
        public PropertyInfo prop;
        public string jsonName;
    }
}
