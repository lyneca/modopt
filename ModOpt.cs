using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using ModOptExtensions;
using ThunderRoad;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using Action = System.Action;
using Object = UnityEngine.Object;
using UnityButton = UnityEngine.UI.Button;

namespace ModOpt {
    public class ModOptions {
        public string name;
        public Dictionary<string, Option> options;


        public ModOptions(string name) {
            options = new Dictionary<string, Option>();
            this.name = name;
        }

        public void AddOption(string title, Option option) {
            options[title] = option;
        }
        public void AddButton(string title, Action action) {
            AddOption(title, new Button(title, action));
        }
        public void AddChoice(string title, params string[] options) {
            AddOption(title, new Choice(title, options.ToList()));
        }
        [Obsolete("Sliders not yet implemented")]
        public void AddSlider(string title, float min = 0, float max = 1, float defaultAmount = 0) {
            AddOption(title, new Slider(title, min, max, defaultAmount));
        }
        public string GetChoice(string title) => options.TryGetValue(title, out var option) ? (option as Choice).Value() : default;
    }

    public abstract class Option {
        public string title;

        public Option(string title) {
            this.title = title;
        }

        public abstract void SetupObject(GameObject obj);
    }

    public class Button : Option {
        public Action action;
        public Button(string title, Action action) : base(title) {
            this.action = action;
        }

        public void Click() {
            action?.Invoke();
        }

        public override void SetupObject(GameObject obj) {
            var button = obj.GetComponent<UnityButton>();
            var text = obj.GetComponent<Text>();
            text.text = title;
            button.onClick.AddListener(Click);
        }
    }

    public class Choice : Option {
        private List<string> options;
        int selected;
        private Text text;
        public Choice(string title, List<string> options) : base(title) {
            this.options = new List<string>(options);
        }

        public void Next() {
            selected++;
            selected %= options.Count;
            text.text = Value();
        }

        public void Prev() {
            if (--selected == -1) {
                selected = options.Count - 1;
            }
            text.text = Value();
        }

        public string Value() => options.ElementAtOrDefault(selected);
        public override void SetupObject(GameObject obj) {
            var prevButton = obj.transform.Find("Prev").GetComponent<UnityButton>();
            var nextButton = obj.transform.Find("Next").GetComponent<UnityButton>();
            var name = obj.transform.Find("Title").GetComponent<Text>();
            text = obj.transform.Find("Text").GetComponent<Text>();
            name.text = title;
            text.text = Value();
            nextButton.onClick.AddListener(Next);
            prevButton.onClick.AddListener(Prev);
        }
    }

    [Obsolete("Not yet implemented")]
    public class Slider : Option {
        private float amount;
        private float min;
        private float max;

        public Slider(string title, float min = 0, float max = 1, float defaultAmount = 0) : base(title) {
            this.min = min;
            this.max = max;
            amount = Mathf.InverseLerp(min, max, defaultAmount);
        }

        public float Value() => Mathf.Lerp(min, max, amount);
        public override void SetupObject(GameObject obj) { throw new System.NotImplementedException(); }
    }

    public class ModOptionsMenu {
        public static ModOptionsMenu local = new ModOptionsMenu();

        public Dictionary<string, ModOptions> mods;
        private string selected;

        public ModOptions Mod(string name) {
            if (mods == null) {
                mods = new Dictionary<string, ModOptions>();
                selected = name;
            }
            if (mods.ContainsKey(name)) {
                return mods[name];
            } else {
                mods[name] = new ModOptions(name);
                return mods[name];
            }
        }

        public ModOptions Selected() => mods[selected];
        public void Select(string name) => selected = name;
        public List<string> ModNames() => mods.Keys.ToList();
        }
    public class ModOptionsModule : MenuModule {
        private GameObject prefabButton;
        private GameObject prefabChoice;
        private Menu menu;
        public override void Init(MenuData menuData, Menu menu) {
            base.Init(menuData, menu);
            this.menu = menu;
            Addressables.LoadAssetAsync<GameObject>("Lyneca.ModOpt.Button").Task.Then(obj => prefabButton = obj);
            Addressables.LoadAssetAsync<GameObject>("Lyneca.ModOpt.Choice").Task.Then(obj => prefabChoice = obj);
        }
        public override State GetState() => State.Enabled;

        public void RefreshModPage() {
            DestroyChildren(menu.GetCustomReference("ModList"));
            foreach (var mod in ModOptionsMenu.local.mods.Values) {
                AddModButton(mod.name);
            }
        }

        public void DestroyChildren(Transform parent) {
            foreach (Transform child in parent) {
                Object.Destroy(child.gameObject);
            }
        }

        public void RefreshSelectedPage() {
            DestroyChildren(menu.GetCustomReference("ModOpts"));
            menu.GetCustomReference("ModTitle").GetComponent<Text>().text = ModOptionsMenu.local.Selected().name;
            foreach (var option in ModOptionsMenu.local.Selected().options.Values) {
                GameObject obj = null;
                if (option is Button) {
                    obj = Object.Instantiate(prefabButton, menu.GetCustomReference("ModOpts"));
                } else if (option is Choice) {
                    obj = Object.Instantiate(prefabChoice, menu.GetCustomReference("ModOpts"));
                }

                if (obj != null) {
                    Debug.Log("Creating object");
                    option.SetupObject(obj);
                }
            }
        }

        public void AddModButton(string name) {
            var obj = Object.Instantiate(prefabButton, menu.GetCustomReference("ModList"));
            obj.GetComponent<Text>().text = name;
            obj.GetComponent<UnityButton>().onClick.AddListener(() => {
                ModOptionsMenu.local.Select(name);
                RefreshSelectedPage();
            });
        }
        public override void OnShow(bool show) {
            base.OnShow(show);
            RefreshModPage();
            RefreshSelectedPage();

            // e.g.
            // ModOptionsMenu.local.Mod("Daggerbending").AddButton("Clear All Daggers", () => {
            //     /*
            //      * foreach (var dagger in daggers) { dagger.Despawn(); }
            //      */
            // });
        }
    }
}
