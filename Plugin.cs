
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using System.Collections;

namespace RecyclePlus
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class RecyclePlusMain : BaseUnityPlugin
    {
        private static readonly bool isDebug = false;
        internal const string ModName = "RecyclePlus";
        internal const string ModVersion = "1.2.0";
        internal const string Author = "TastyChickenLegs";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource TastyLogger =
            BepInEx.Logging.Logger.CreateLogSource(ModName);
        
        /// <summary>
        /// Custom Entered Variables
        /// </summary>
        public static ConfigEntry<KeyboardShortcut> modKey;
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<float> returnResources;
        public static ConfigEntry<bool> ConfirmDialog;

        //public static ConfigEntry<KeyboardShortcut> TrashHotkey;
        public static ConfigEntry<SoundEffect> Sfx;

        public static ConfigEntry<Color> TrashColor;
        public static ConfigEntry<string> TrashLabel;

        public static bool _clickedTrash = false;
        public static bool _confirmed = false;
        public static InventoryGui _gui;
        public static Sprite trashSprite;
        public static Sprite bgSprite;
        public static GameObject dialog;
        public static AudioClip[] sounds = new AudioClip[3];
        public static Transform trash;
        public static AudioSource audio;
        public static TrashButton trashButton;
        public static bool configVerifyClient => _configVerifyClient.Value;
        public static ManualLogSource MyLogger;
        public static RecyclePlusMain context;
        public static ConfigEntry<bool> showcan;
        public static ConfigEntry<bool> _configVerifyClient;
        /// <summary>
        /// End Custom Variables
        /// </summary>

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug)
                Debug.Log((pref ? typeof(RecyclePlusMain).Namespace + " " : "") + str);
        }


        private static readonly ConfigSync ConfigSync = new(ModGUID)
            { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

        public enum Toggle
        {
            On = 1,
            Off = 0
        }
        public enum SoundEffect
        {
            None,
            Sound1,
            Sound2,
            Sound3,
            Random
        }
        public static void Log(string msg)
        {
            MyLogger?.LogInfo(msg);
        }

        public static void LogErr(string msg)
        {
            MyLogger?.LogError(msg);
        }

        public static void LogWarn(string msg)
        {
            MyLogger?.LogWarning(msg);
        }
        public void Awake()
        {
            _serverConfigLocked = config("", "Lock Configuration", Toggle.On,
                "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
            /// Custom Configs ///
            TrashLabel = config("General", "TrashLabel", "Trash", "Label for the trash button");
            _configVerifyClient = config("", "Verify Client", false, "Verify the client for connected servers.");
            returnResources = config("General", "ReturnResources", 1f, new ConfigDescription
                ("Fraction of resources to return (0.0 - 1.0) 1.0 is 100%", new AcceptableValueRange<float>(0.0f, 1.0f)));
            showcan = config("General", "ShowTrashCan", true, new ConfigDescription("Show Trash Can on the inventory menu Requires Restart"));
            modKey = config("General", "DiscardHotkey", new KeyboardShortcut(KeyCode.Delete),
                new ConfigDescription("The modifier key to recycle or delete on click", new AcceptableShortcuts()));
            TrashColor = config("General", "TrashColor", new Color(1f, 0.8482759f, 0), "Color for the trash label");
            TrashLabel.SettingChanged += (sender, args) => { if (trashButton != null) { trashButton.SetText(TrashLabel.Value); } };

            TastyLogger.LogInfo(nameof(RecyclePlusMain) + " Loaded!");

            trashSprite = LoadSprite("RecyclePlus.Resources.trash.png", new Rect(0, 0, 64, 64), new Vector2(32, 32));
            bgSprite = LoadSprite("RecyclePlus.Resources.trashmask.png", new Rect(0, 0, 96, 112), new Vector2(48, 56));

            sounds[0] = LoadAudioClip("RecyclePlus.Resources.trash1.wav");
            sounds[1] = LoadAudioClip("RecyclePlus.Resources.trash2.wav");
            sounds[2] = LoadAudioClip("RecyclePlus.Resources.trash3.wav");
            /// End of Custom Configs ///
            Harmony.CreateAndPatchAll(typeof(RecyclePlusMain));



            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                TastyLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                TastyLogger.LogError($"There was an issue loading your {ConfigFileName}");
                TastyLogger.LogError("Please check your config entries for spelling and format!");
            }
        }


        #region ConfigOptions

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            public bool? Browsable = false;
        }
        
        class AcceptableShortcuts : AcceptableValueBase
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
            {
            }

            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() =>
                "# Acceptable values: " + string.Join(", ", KeyboardShortcut.AllKeyCodes);
        }

        #endregion

        public static AudioClip LoadAudioClip(string path)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream audioStream = asm.GetManifestResourceStream(path);

            using (MemoryStream mStream = new MemoryStream())
            {
                audioStream.CopyTo(mStream);
                return WavUtility.ToAudioClip(mStream.GetBuffer());
            }
        }

        public static Sprite LoadSprite(string path, Rect size, Vector2 pivot, int units = 100)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream trashImg = asm.GetManifestResourceStream(path);

            Texture2D tex = new Texture2D((int)size.width, (int)size.height, TextureFormat.RGBA32, false, true);

            using (MemoryStream mStream = new MemoryStream())
            {
                trashImg.CopyTo(mStream);
                tex.LoadImage(mStream.ToArray());
                tex.Apply();
                return Sprite.Create(tex, size, pivot, units);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Show")]
        [HarmonyPostfix]
        public static void Show_Postfix(InventoryGui __instance)
        {
            if (!showcan.Value)
                return;

            Transform playerInventory = InventoryGui.instance.m_player.transform;
            trash = playerInventory.Find("Trash");

            if (trash != null)
                return;

            _gui = InventoryGui.instance;

            trash = Instantiate(playerInventory.Find("Armor"), playerInventory);
            trashButton = trash.gameObject.AddComponent<TrashButton>();

            var guiMixer = AudioMan.instance.m_masterMixer.FindMatchingGroups("GUI")[0];

            audio = trash.gameObject.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.outputAudioMixerGroup = guiMixer;
            audio.bypassReverbZones = true;
        }
        public class TrashButton : MonoBehaviour
        {
            private Canvas canvas;
            private GraphicRaycaster raycaster;
            private RectTransform rectTransform;
            private GameObject buttonGo;

            private void Awake()
            {
                if (InventoryGui.instance == null)
                    return;

                var playerInventory = InventoryGui.instance.m_player.transform;
                RectTransform rect = GetComponent<RectTransform>();
                rect.anchoredPosition -= new Vector2(0, 78);

                //set the color to Valheim Yellow
                //TrashColor = new Color(1f, 0.8482759f, 0);

                SetText(TrashLabel.Value);
                SetColor(TrashColor.Value);

                Transform tArmor = transform.Find("armor_icon");
                if (!tArmor)
                {
                    LogErr("armor_icon not found!");
                }
                tArmor.GetComponent<Image>().sprite = trashSprite;

                transform.SetSiblingIndex(0);
                transform.gameObject.name = "Trash";

                buttonGo = new GameObject("ButtonCanvas");
                rectTransform = buttonGo.AddComponent<RectTransform>();
                rectTransform.transform.SetParent(transform.transform, true);
                rectTransform.anchoredPosition = Vector2.zero;
                rectTransform.sizeDelta = new Vector2(70, 74);
                canvas = buttonGo.AddComponent<Canvas>();
                raycaster = buttonGo.AddComponent<GraphicRaycaster>();

                // Add trash ui button
                Button button = buttonGo.AddComponent<Button>();
                button.onClick.AddListener(new UnityAction(RecyclePlusMain.TrashItem));
                var image = buttonGo.AddComponent<Image>();
                image.color = new Color(0, 0, 0, 0);

                // Add border background
                Transform frames = playerInventory.Find("selected_frame");
                GameObject newFrame = Instantiate(frames.GetChild(0).gameObject, transform);
                newFrame.GetComponent<Image>().sprite = bgSprite;
                newFrame.transform.SetAsFirstSibling();
                newFrame.GetComponent<RectTransform>().sizeDelta = new Vector2(-8, 22);
                newFrame.GetComponent<RectTransform>().anchoredPosition = new Vector2(6, 7.5f);

                // Add inventory screen tab
                UIGroupHandler handler = gameObject.AddComponent<UIGroupHandler>();
                handler.m_groupPriority = 1;
                handler.m_enableWhenActiveAndGamepad = newFrame;
                _gui.m_uiGroups = _gui.m_uiGroups.AddToArray(handler);

                gameObject.AddComponent<TrashHandler>();
            }

            private void Start()
            {
                StartCoroutine(DelayedOverrideSorting());
            }

            private IEnumerator DelayedOverrideSorting()
            {
                yield return null;

                if (canvas == null) yield break;

                canvas.overrideSorting = true;
                canvas.sortingOrder = 1;
            }

            public void SetText(string text)
            {
                Transform tText = transform.Find("ac_text");
                if (!tText)
                {
                    LogErr("ac_text not found!");
                    return;
                }
                tText.GetComponent<Text>().text = text;
            }

            public void SetColor(Color color)
            {
                Transform tText = transform.Find("ac_text");
                if (!tText)
                {
                    LogErr("ac_text not found!");
                    return;
                }
                tText.GetComponent<Text>().color = color;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        [HarmonyPostfix]
        public static void Postfix()
        {
            OnCancel();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryGui), "UpdateItemDrag")]
        public static void UpdateItemDrag_Postfix(InventoryGui __instance, ref GameObject ___m_dragGo, ItemDrop.ItemData ___m_dragItem, Inventory ___m_dragInventory, int ___m_dragAmount)
        {
            if (modKey.Value.IsDown())
            {
                _clickedTrash = true;
            }

            if (_clickedTrash && ___m_dragItem != null && ___m_dragInventory.ContainsItem(___m_dragItem))
            {
                Dbgl($"Recycling {___m_dragAmount}/{___m_dragItem.m_stack} {___m_dragItem.m_dropPrefab.name}");

                bool returnedGoods = false;

                if (returnResources.Value > 0)
                {
                    Recipe recipe = ObjectDB.instance.GetRecipe(___m_dragItem);

                    if (recipe != null)
                        //Dbgl($"Recipe stack: {recipe.m_amount} num of stacks: {___m_dragAmount / recipe.m_amount}");

                        if (recipe != null && ___m_dragAmount / recipe.m_amount > 0)
                        {
                            //items are gettng returned set up the message back to player for Recycled//
                            returnedGoods = true;

                            for (int i = 0; i < ___m_dragAmount / recipe.m_amount; i++)
                            {
                                foreach (Piece.Requirement req in recipe.m_resources)
                                {
                                    int quality = ___m_dragItem.m_quality;
                                    for (int j = quality; j > 0; j--)
                                    {
                                        GameObject prefab = ObjectDB.instance.m_items.FirstOrDefault(item => item.GetComponent<ItemDrop>
                                            ().m_itemData.m_shared.m_name == req.m_resItem.m_itemData.m_shared.m_name);

                                        ItemDrop.ItemData newItem = prefab.GetComponent<ItemDrop>().m_itemData;
                                        int numToAdd = Mathf.RoundToInt(req.GetAmount(j) * returnResources.Value);
                                        Dbgl($"Returning {numToAdd}/{req.GetAmount(j)} {prefab.name}");
                                        while (numToAdd > 0)
                                        {
                                            int stack = Mathf.Min(req.m_resItem.m_itemData.m_shared.m_maxStackSize, numToAdd);
                                            numToAdd -= stack;

                                            if (Player.m_localPlayer.GetInventory().AddItem(prefab.name, stack, req.m_resItem.m_itemData.m_quality,
                                                req.m_resItem.m_itemData.m_variant, 0, "") == null)
                                            {
                                                ItemDrop component = Instantiate(prefab, Player.m_localPlayer.transform.position
                                                    + Player.m_localPlayer.transform.forward +
                                                    Player.m_localPlayer.transform.up, Player.m_localPlayer.transform.rotation).GetComponent<ItemDrop>();

                                                component.m_itemData = newItem.Clone();
                                                component.m_itemData.m_dropPrefab = prefab;
                                                component.m_itemData.m_stack = stack;
                                                Traverse.Create(component).Method("Save").GetValue();
                                            }
                                        }
                                    }
                                }
                            }

                            //show a message to the player that the item is deleted//
                        }
                }

                if (___m_dragAmount == ___m_dragItem.m_stack)
                {
                    Player.m_localPlayer.RemoveEquipAction(___m_dragItem);
                    Player.m_localPlayer.UnequipItem(___m_dragItem, false);
                    ___m_dragInventory.RemoveItem(___m_dragItem);

                    //if items were recycled let the player know otherwise stuff was put in the trash//

                    if (returnedGoods)
                        Player.m_localPlayer.Message(MessageHud.MessageType.Center, string.Format("Item Recycled"), 0, null);
                    else
                        Player.m_localPlayer.Message(MessageHud.MessageType.Center, string.Format("Item Deleted"), 0, null);
                }
                else
                {
                    //stuff deleted let the player know if was trashed.//
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, string.Format("Item Deleted"), 0, null);
                    ___m_dragInventory.RemoveItem(___m_dragItem, ___m_dragAmount);
                }
                if (audio != null)
                {
                    audio.PlayOneShot(sounds[Random.Range(0, 3)]);
                }
                //refresh the inventory screen and play sound above //
                __instance.GetType().GetMethod("SetupDragItem", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(__instance, new object[] { null, null, 0 });
                __instance.GetType().GetMethod("UpdateCraftingPanel", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(__instance, new object[] { false });
            }

            _clickedTrash = false;
        }

        public static void OnConfirm()
        {
            _confirmed = true;
            if (dialog != null)
            {
                Destroy(dialog);
                dialog = null;
            }

            TrashItem();
        }

        public static void OnCancel()
        {
            _confirmed = false;
            if (dialog != null)
            {
                Destroy(dialog);
                dialog = null;
            }
        }

        public static void TrashItem()
        {
            Log("Trash Items clicked!");

            if (_gui == null)
            {
                LogErr("_gui is null");
                return;
            }

            _clickedTrash = true;

            _gui.GetType().GetMethod("UpdateItemDrag", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_gui, new object[] { });
        }
    }

    public class TrashHandler : MonoBehaviour
    {
        private UIGroupHandler handler;

        private void Awake()
        {
            handler = this.GetComponent<UIGroupHandler>();
        }

        private void Update()
        {
            if (ZInput.GetButtonDown("JoyButtonA") && handler.IsActive)
            {
                RecyclePlusMain.TrashItem();
                // Switch back to inventory iab
                typeof(InventoryGui).GetMethod("SetActiveGroup", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(InventoryGui.instance, new object[] { 1 });
            }
        }
    }
}
