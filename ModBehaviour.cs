using AzusaModle;
using AzusaSdModle;
using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.Buffs;
using Duckov.Modding;
using Duckov.UI.DialogueBubbles;
using FMOD;
using FMODUnity;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace AzusaSdModle
{
    internal class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        [Header("Buff配置")]
        //参数
        public float Charge_Interval = 18f;// 获取时间
        public int Max_Charges = 3;// 最大层数
        private int Current_Charges = 0;// 目前层数
        private float Charge_Timer = 0f;// 目前计时
        private bool Is_Charging = true;
        private bool Is_BuffLoaded = false;
        private static bool Is_Activated = false;
        private bool Is_GetBuff = true;
        // 图标
        private const string Icon_T1 = "MinorSkill_T1.png";
        private const string Icon_T2 = "MinorSkill_T2.png";
        private const string Icon_T3 = "MinorSkill_T3.png";
        //
        private static readonly object[] chargeSources = new object[4];
        private static Sprite[] ChargeIcon = new Sprite[4];
        private static Buff[] ChargeBuffTemplate = new Buff[4];
        private static CharacterBuffManager buffManager = (CharacterBuffManager)null;

        [Header("模型变量")]
        private string bundleName = "azusa_sd";
        private string assetName = "Character_Azusa_sd";
        private string targetShaderName = "SodaCraft/SodaCharacter";
        private CharacterModel? characterModel;
        private Movement? movement;
        private string[] pathsToHide = new string[9]
        {
         "CustomFaceInstance/DuckBody",
         "CustomFaceInstance/Armature/Root/Pelvis/Thigh.L",
         "CustomFaceInstance/Armature/Root/Pelvis/Thigh.R",
         "CustomFaceInstance/Armature/Root/Pelvis/Spine.001/Spine.002/Spine.003/Spine.004/Head",
         "CustomFaceInstance/Armature/Root/Pelvis/Spine.001/Spine.002/Spine.003/Spine.004/UpperArm.L/Wings.L",
         "CustomFaceInstance/Armature/Root/Pelvis/Spine.001/Spine.002/Spine.003/Spine.004/UpperArm.R/Wings.R",
         "CustomFaceInstance/Armature/Root/Pelvis/Spine.001/Spine.002/Spine.003/Spine.004/ArmorSocket",
         "CustomFaceInstance/Armature/Root/Pelvis/Spine.001/Spine.002/Spine.003/Spine.004/BackpackSocket",
         "CustomFaceInstance/Armature/Root/Pelvis/TailSocket/"
        };
        private List<GameObject> hideGameObject = new List<GameObject>();
        private List<MeshRenderer> mrToHide = new List<MeshRenderer>();
        private GameObject? AzusaInstance;// 实例化后的角色对象
        private Animator? AzusaAnimator;// 新角色的动画控制器

        // 武器
        private Transform? originalRightHandSocket;
        private Transform? weaponAnchor;
        private Transform? ModleweaponAnchor;
        // 音效
        private static string _selfPath = "";
        private static FMOD.Channel AzusaVoiceChannel;
        private static Sound _tacticVoice1;
        private static Sound _tacticVoice2;
        private static Sound _hurtVoice1;
        private static Sound _hurtVoice2;
        private static Sound _ExSkill1;
        private static Sound _defeatVoice;
        private static System.Random random = new System.Random();

        private bool recordModle;
        private bool isModelLoaded;
        private bool isSetModel;
        private bool isTriggerInput;
        private bool isTriggerDown;
        private bool isGunActive = true;

        private bool wasRunning;
        private bool wasDashing;
        private bool wasMoving;

        public float Volume = 1f;//角色声音大小
        public bool ModelWeapons = false;//模型武器显示
        public bool PlaySoundEffect = true;//是否开启角色声音
        public int HurtVoiceProbability = 1;//随机伤害音效概率

        private void OnEnable()
        {
            ModLog.Log("已启用Azusa[白洲梓]...");
            if (string.IsNullOrEmpty(_selfPath))
            {
                _selfPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            LoadVoiceResources();
            Health.OnHurt += OnCharacterHurt;
            LevelManager.OnAfterLevelInitialized += OnSceneLoaded;
            LevelManager.OnAfterLevelInitialized += LoadFrostcraftIcons;
            ModManager.OnModActivated += OnModActivated;
            if (!ModConfigAPI.IsAvailable())
                return;
            this.SetupModConfig();
        }
        private void OnDisable()
        {
            ModLog.Log("已禁用Azusa[白洲梓]...");
            Health.OnHurt -= OnCharacterHurt;
            LevelManager.OnAfterLevelInitialized -= OnSceneLoaded;
            LevelManager.OnAfterLevelInitialized -= LoadFrostcraftIcons;
            ModManager.OnModActivated -= OnModActivated;
            ModConfigAPI.SafeRemoveOnOptionsChangedDelegate(OnOptionsChanged);
            RestoreWeaponSocket();
        }
        private void OnSceneLoaded()
        {
            this.isModelLoaded = false;
            this.Is_BuffLoaded = true;
            if (!this.isModelLoaded)
            {
                StartCoroutine(DelayedAutoLoad());
            }
            this.isSetModel = true;
            ModLog.Log($"当前是否是基地关卡: {LevelManager.Instance.IsBaseLevel}");
            if (LevelManager.Instance.IsBaseLevel || this.PlaySoundEffect)
            {
                ModLog.Log("满足播放条件，尝试播放入场音效");
                PlayTacticVoice();
            }

        }
        private void Update()
        {
            this.recordModle = this.isSetModel;
            if (this.characterModel != null && this.Is_GetBuff)
                UpdateCharges_Buff();
            if (this.isSetModel)
                this.UpdateAnimation();
            if (!Input.GetKey(KeyCode.LeftControl) || !Input.GetKeyDown(KeyCode.Z))
                return;
            this.isGunActive = !this.isGunActive;
            this.LoadAllHoldingItemRenderers();
            this.ReloadHoldingVisual();
        }
        /// <summary>
        /// 设置系统
        /// </summary>
        protected virtual void OnAfterSetup()
        {
            ModLog.Log("Azusa[白洲梓]已加载 等待ModConfig...");
            ModManager.OnModActivated += OnModActivated;
            if (!ModConfigAPI.IsAvailable())
                return;
            this.SetupModConfig();
        }
        private void OnModActivated(ModInfo info, Duckov.Modding.ModBehaviour behaviour)
        {
            if (!(info.name == ModConfigAPI.ModConfigName))
                return;
            ModLog.Log("[Azusa[白洲梓]检测到ModConfig已激活");
            this.SetupModConfig();
        }
        private void SetupModConfig()
        {
            ModConfigAPI.SafeAddInputWithSlider("AzusaSdModle", "ConfigBuffGetTime", "Buff获取时间", typeof(int), 18, new Vector2?(new Vector2(5, 60)));
            ModConfigAPI.SafeAddInputWithSlider("AzusaSdModle", "ConfigHurtVoiceProbability", "伤害音效概率[0大~10小]", typeof(int), 1, new Vector2?(new Vector2(0, 10)));
            ModConfigAPI.SafeAddInputWithSlider("AzusaSdModle", "ConfigVolume", "声音音量[0~1]", typeof(float), 1f, new Vector2?(new Vector2(0f, 1f)));
            ModConfigAPI.SafeAddBoolDropdownList("AzusaSdModle", "ConfigGetExclusiveBuff", "开启获取Buff", true);
            ModConfigAPI.SafeAddBoolDropdownList("AzusaSdModle", "ConfigPlaySoundEffect", "开启声音",true);

            this.LoadConfig();
            ModConfigAPI.SafeAddOnOptionsChangedDelegate(OnOptionsChanged);
        }
        private void OnOptionsChanged(string changedKey)
        {
            if (!changedKey.EndsWith("ConfigGetExclusiveBuff") && !changedKey.EndsWith("ConfigBuffGetTime") && !changedKey.EndsWith("ConfigHurtVoiceProbability") && !changedKey.EndsWith("ConfigVolume") && !changedKey.EndsWith("ConfigPlaySoundEffect"))
                return;
            this.LoadConfig();
        }
        private void LoadConfig()
        {
            this.Is_GetBuff = ModConfigAPI.SafeLoad<bool>("AzusaSdModle", "ConfigGetExclusiveBuff", true);
            this.Charge_Interval = ModConfigAPI.SafeLoad<int>("AzusaSdModle", "ConfigBuffGetTime", 1);
            this.HurtVoiceProbability = ModConfigAPI.SafeLoad<int>("AzusaSdModle", "ConfigHurtVoiceProbability", 1);
            this.Volume = ModConfigAPI.SafeLoad<float>("AzusaSdModle", "ConfigVolume", 1f);
            this.PlaySoundEffect = ModConfigAPI.SafeLoad<bool>("AzusaSdModle", "ConfigPlaySoundEffect", true);
            ModLog.Log($"Azusa[白洲梓]配置加载完成: 声音大小{this.Volume}, 是否开启角色声音{this.PlaySoundEffect}, 随机伤害音效概率{this.HurtVoiceProbability}, Buff获取时间{this.Charge_Interval}，开启获取Buff{this.Is_GetBuff}");
            if (!this.Is_GetBuff)
            {
                this.ResetBuff();
            }
        }
        /// <summary>
        /// 声音系统
        /// </summary>
        private static void LoadVoiceResources()
        {
            ModLog.Log("开始加载音效资源...");
            try
            {
                string tacticPath1 = Path.Combine(_selfPath, "Voice", "Tactic1.ogg");
                string tacticPath2 = Path.Combine(_selfPath, "Voice", "Tactic2.ogg");
                string hurt1Path = Path.Combine(_selfPath, "Voice", "Hurt1.ogg");
                string hurt2Path = Path.Combine(_selfPath, "Voice", "Hurt2.ogg");
                string exskill1Path = Path.Combine(_selfPath, "Voice", "ExSkill1.ogg");
                string defeatPath = Path.Combine(_selfPath, "Voice", "Defeat1.ogg");
                ModLog.Log("音效文件是否存在");
                ModLog.Log($"- Tactic1.ogg: {File.Exists(tacticPath1)}");
                ModLog.Log($"- Tactic2.ogg: {File.Exists(tacticPath2)}");
                ModLog.Log($"- Hurt1.ogg: {File.Exists(hurt1Path)}");
                ModLog.Log($"- Hurt2.ogg: {File.Exists(hurt2Path)}");
                ModLog.Log($"- ExSkill1.ogg: {File.Exists(exskill1Path)}");
                ModLog.Log($"- Defeat1.ogg: {File.Exists(defeatPath)}");
                // 入场音效
                FMOD.System coreSystem = RuntimeManager.CoreSystem;
                ((FMOD.System)coreSystem).createSound(tacticPath1, MODE.DEFAULT, out _tacticVoice1);
                ((FMOD.System)coreSystem).createSound(tacticPath2, MODE.DEFAULT, out _tacticVoice2);
                // 受伤音效
                ((FMOD.System)coreSystem).createSound(hurt1Path, MODE.DEFAULT, out _hurtVoice1);
                ((FMOD.System)coreSystem).createSound(hurt2Path, MODE.DEFAULT, out _hurtVoice2);
                // 技能音效
                ((FMOD.System)coreSystem).createSound(exskill1Path, MODE.DEFAULT, out _ExSkill1);
                // 死亡音效
                ((FMOD.System)coreSystem).createSound(defeatPath, MODE.DEFAULT, out _defeatVoice);
                ModLog.Log("音效资源加载完成");
            }
            catch (Exception ex)
            {
                ModLog.Error($"加载音效资源时出错: {ex.Message}");
            }
        }
        private void PlayTacticVoice()
        {
            if (this.PlaySoundEffect)
            {
                FMOD.System coreSystem = RuntimeManager.CoreSystem;
                ModBehaviour.AzusaVoiceChannel.setVolume(this.Volume);
                coreSystem.playSound(_tacticVoice1, new ChannelGroup(), false, out AzusaVoiceChannel);
                ModBehaviour.AzusaVoiceChannel.setVolume(this.Volume);
            }
        }
        private void OnCharacterHurt(Health health, DamageInfo damageInfo)
        {
            if (!this.PlaySoundEffect || health.team != 0)
                return;
            if (random.Next(this.HurtVoiceProbability) != 0)
                return;
            FMOD.System coreSystem = RuntimeManager.CoreSystem;
            ModBehaviour.AzusaVoiceChannel.setVolume(this.Volume);
            Sound[] hurtVoices = { _hurtVoice1, _hurtVoice2};
            Sound selectedVoice = hurtVoices[random.Next(hurtVoices.Length)];
            coreSystem.playSound(selectedVoice, new ChannelGroup(), false, out AzusaVoiceChannel);
            ModBehaviour.AzusaVoiceChannel.setVolume(this.Volume);
        }
        /// <summary>
        /// 角色系统
        /// </summary>
        private void RestoreModel()
        {
            this.RestoreDuck();
            this.UnloadAzusa();
            this.ResetAnimation();
            this.RestoreWeaponSocket();
        }
        private void RestoreDuck()
        {
            foreach (GameObject gameObject in this.hideGameObject)
                gameObject.SetActive(true);
            this.hideGameObject.Clear();
        }
        private void UnloadAzusa()
        {
            if (this.AzusaInstance != null)
            {
                UnityEngine.Object.Destroy(this.AzusaInstance);
                this.AzusaInstance = null;
                ModLog.Log("已卸载Azusa[白洲梓]模型");
            }
            this.AzusaAnimator = null;
        }
        private void ResetAnimation()
        {
            this.wasMoving = false;
            this.wasRunning = false;
        }

        private IEnumerator DelayedAutoLoad()
        {
            yield return new WaitForSeconds(0.05f);
            ModLog.Log("Azusa[白洲梓] 自动加载模型");
            SetModel();
            this.isModelLoaded = true;
        }
        private void SetModel()
        {
            ModLog.Log("Azusa[白洲梓] 获取模型数据");
            this.characterModel = LevelManager.Instance.MainCharacter.characterModel;
            this.movement = this.characterModel.characterMainControl.movementControl;
            this.wasRunning = this.movement.Running;
            this.wasMoving = this.movement.Moving;
            this.ResetBuff();
            this.HideDuck();
            this.StartCoroutine(this.LoadAzusaCoroutine());
        }

        private void UpdateAnimation()// 动画
        {
            if (this.AzusaAnimator == null)
                return;

            bool moving = this.movement.Moving;
            bool running = this.movement.Running;
            bool dashing = this.characterModel.characterMainControl.Dashing;

            if (dashing && dashing != this.wasDashing)
                this.AzusaAnimator.Play("Dash");

            Vector3 velocity = CharacterMainControl.Main.Velocity;
            float speed = Mathf.Lerp(0.1f, 1.3f, velocity.magnitude / 7f);
            this.AzusaAnimator.SetBool("Dashing", dashing);
            this.AzusaAnimator.SetBool("Running", movement.Moving);
            if (moving | running)
                this.AzusaAnimator.SetFloat("MovingSpeed", speed);
            else
                this.AzusaAnimator.SetFloat("MovingSpeed", 0.0f);

            this.wasMoving = moving;
            this.wasRunning = running;
            this.wasDashing = dashing;

            this.AzusaAnimator.SetBool("Aiming", CharacterMainControl.Main.IsInAdsInput || this.isTriggerInput);
            this.AzusaAnimator.SetFloat("Attack", this.isTriggerDown ? 0.99f : 0.0f);
        }

        private void HideDuck()
        {
            Transform transform1 = this.characterModel.transform;
            this.hideGameObject.Clear();
            foreach (string str in this.pathsToHide)
            {
                Transform transform2 = transform1.Find(str);
                if (transform2 != null)
                {
                    this.hideGameObject.Add(transform2.gameObject);
                    transform2.gameObject.SetActive(false);
                    ModLog.Log("已隐藏: " + str);
                }
                else
                    ModLog.Warning("未找到对象: " + str);
            }
        }

        private IEnumerator LoadAzusaCoroutine()
        {
            string path = Path.Combine(this.info.path, this.bundleName);
            if (!File.Exists(path))
            {
                ModLog.Error("AssetBundle不存在: " + path);
            }
            else
            {
                AssetBundleCreateRequest bundleRequest = AssetBundle.LoadFromFileAsync(path);
                yield return bundleRequest;
                AssetBundle bundle = bundleRequest.assetBundle;
                if (bundle == null)
                {
                    ModLog.Error("加载AssetBundle失败: " + this.bundleName);
                }
                else
                {
                    ModLog.Log("成功加载AssetBundle: " + this.bundleName);
                    AssetBundleRequest assetRequest = bundle.LoadAssetAsync<GameObject>(this.assetName);
                    yield return assetRequest;
                    if (assetRequest.asset is GameObject asset && this.characterModel != null)
                        this.InitializeCharacter(asset);
                    else
                        ModLog.Error("实例化失败：模型或角色为空");
                    bundle.Unload(false);
                }
            }
        }
        private void InitializeCharacter(GameObject Azusa)
        {
            this.ReplaceAllShaders(Azusa, this.targetShaderName);
            Azusa.layer = LayerMask.NameToLayer("Default");
            this.AzusaInstance = UnityEngine.Object.Instantiate(Azusa, this.characterModel.transform);
            this.AzusaInstance.transform.localScale = Vector3.one * 1.3f;
            this.AzusaInstance.transform.position += Vector3.forward * 0.1f;
            this.AzusaAnimator = this.AzusaInstance.GetComponent<Animator>();
            SetWeaponSocket();
            if (this.ModleweaponAnchor)
                ModleweaponAnchor.gameObject.SetActive(!isGunActive);
                this.LoadAllHoldingItemRenderers();
                this.ReloadHoldingVisual();

            //StartCoroutine(LoadPaperBoxReplacementCoroutine());

            ModLog.Log("成功加载并挂载角色: " + this.assetName);
        }
        private void ReplaceAllShaders(GameObject target, string shaderName)
        {
            Shader targetShader = Shader.Find(shaderName);
            if (targetShader == null)
                ModLog.Error("找不到着色器: " + shaderName);
            else
                this.ProcessRenderersRecursive(target, targetShader);
        }
        private void ProcessRenderersRecursive(GameObject obj, Shader targetShader)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
                this.ReplaceRendererShaders(renderer, targetShader);
            foreach (Transform component in obj.transform)
                this.ProcessRenderersRecursive(component.gameObject, targetShader);
        }
        private void ReplaceRendererShaders(Renderer renderer, Shader targetShader)
        {
            foreach (Material material in renderer.materials)
            {
                if (material != null)
                {
                    if (!material.name.Contains("Azusa_Original_Mouth"))
                    {
                        material.shader = targetShader;
                        ModLog.Log($"替换材质着色器: {material.name} -> {targetShader.name}");
                    }
                    if (material.name.Contains("Azusa_Original_Mouth"))
                    {
                        ModLog.Log("你这必嘴巴怎么难搞材质😭😭");
                    }
                }
            }
        }

        private void LoadAllHoldingItemRenderers()
        {
            Transform rightHandSocket = CharacterMainControl.Main.RightHandSocket;
            this.mrToHide.Clear();
            this.LoadRenderers(rightHandSocket);
        }
        private void LoadRenderers(Transform target)
        {
            MeshRenderer component = target.GetComponent<MeshRenderer>();
            if (component != null)
                this.mrToHide.Add(component);
            if (target.childCount == 0)
                return;
            for (int index = 0; index < target.childCount; ++index)
                this.LoadRenderers(target.GetChild(index));
        }

        private void ReloadHoldingVisual()
        {
            for (int index = 0; index < this.mrToHide.Count; ++index)
                this.mrToHide[index].enabled = this.isGunActive;
            if (this.ModleweaponAnchor)
                ModleweaponAnchor.gameObject.SetActive(!isGunActive);
        }

        private void SetWeaponSocket()
        {
            if (this.AzusaInstance == null) return;
            this.weaponAnchor = this.AzusaInstance.transform.Find("Azusa_Original_Mesh/bone_root/Bip001/Bip001_Weapon");
            this.ModleweaponAnchor = this.AzusaInstance.transform.Find("Azusa_Original_Weapon");
            if (this.weaponAnchor == null)
            {
                ModLog.Error("未找到Bip001_Weapon武器锚点");
                return;
            }
            try
            {
                GameObject correctionNode = new GameObject("WeaponCorrection");
                correctionNode.transform.SetParent(this.weaponAnchor, false);
                correctionNode.transform.localPosition = Vector3.zero;// 重置位置和旋转
                correctionNode.transform.localPosition = new Vector3(0f, 0.065f, 0f);
                correctionNode.transform.localRotation = Quaternion.identity;
                correctionNode.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);//旋转修正）
                correctionNode.transform.localScale = new Vector3(0.65f, 0.65f, 0.65f);
                FieldInfo field = typeof(CharacterModel).GetField("rightHandSocket",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    this.originalRightHandSocket = (Transform)field.GetValue(this.characterModel);
                    field.SetValue(this.characterModel, correctionNode.transform);// 设置武器锚点
                    ModLog.Log("成功设置武器锚点到Bip001_Weapon");
                    ForceRefreshWeapon();
                }
                else
                {
                    ModLog.Error("未找到rightHandSocket字段");
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"设置武器锚点时出错: {ex.Message}");
            }
        }
        private void RestoreWeaponSocket()
        {
            if (this.originalRightHandSocket == null || this.characterModel == null)
            {
                ModLog.Warning("无法恢复原始武器插槽");
                return;
            }
            try
            {
                FieldInfo field = typeof(CharacterModel).GetField("rightHandSocket",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(this.characterModel, this.originalRightHandSocket);
                    ModLog.Log("已恢复原始武器插槽");
                    ForceRefreshWeapon();
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"恢复武器插槽时出错: {ex.Message}");
            }
        }
        private void ForceRefreshWeapon()
        {
            if (this.characterModel?.characterMainControl == null) return;
            var currentItem = this.characterModel.characterMainControl.CurrentHoldItemAgent;
            if (currentItem != null)
            {
                Item item = ((ItemAgent)currentItem).Item;
                if (item != null)
                {
                    this.characterModel.characterMainControl.ChangeHoldItem(null);
                    this.characterModel.characterMainControl.ChangeHoldItem(item);
                    ModLog.Log("武器模型已刷新");
                }
            }
        }

        private void OnDestroy() => ModLog.Log("OnDestroy!");
        /// <summary>
        /// Buff系统
        /// </summary>
        private void ResetBuff()
        {
            if (Current_Charges > 0)
            {
                ModLog.Log("Azusa[白洲梓] 重置buff");
                CharacterMainControl character = CharacterMainControl.Main;
                Item characterItem = CharacterMainControl.Main?.CharacterItem;
                characterItem.GetStat(GUN_DAMAGE_MULTIPLIER)?.RemoveAllModifiersFromSource(chargeSources[Current_Charges]);
                characterItem.GetStat(GUN_CRIT_RATE_GAIN)?.RemoveAllModifiersFromSource(chargeSources[Current_Charges]);
                characterItem.GetStat(GUN_CRIT_DAMAGE_GAIN)?.RemoveAllModifiersFromSource(chargeSources[Current_Charges]);
                ModBehaviour.ChargeBuffTemplate = new Buff[4];
                this.Current_Charges = 0;
                if (character.GetBuffManager().HasBuff(149122616))
                {               
                    ModLog.Log("Azusa[白洲梓] 移除指定buff ID:149122616");
                    character.GetBuffManager().RemoveBuff(149122616,true);
                }
                this.Is_Charging = true;
                
            }
        }
        private IEnumerator DelayedInitialization()
        {
            yield return new WaitForSeconds(0.1f);
            if (!Is_Activated)
            {
                Is_Activated = true;
                for (int i = 1; i <= 3; i++)
                {
                    chargeSources[i] = new object();
                }
                this.LoadFrostcraftIcons();
            }
        }
        private void LoadFrostcraftIcons()
        {
            try
            {
                string str = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Icon");
                if (!Directory.Exists(str))
                {
                    ModLog.Log("Azusa[白洲梓]图标Buff文件夹未找到: " + str);
                }
                else
                {
                    ChargeIcon[1] = this.LoadIconFromFile(str, "MinorSkill_T1.png");
                    ChargeIcon[2] = this.LoadIconFromFile(str, "MinorSkill_T2.png");
                    ChargeIcon[3] = this.LoadIconFromFile(str, "MinorSkill_T3.png");
                }
            }
            catch (Exception ex)
            {
                ModLog.Log("Azusa[白洲梓]加载Buff图标时出错: " + ex.Message);
            }
        }
        private Sprite LoadIconFromFile(string iconsDirectory, string fileName)
        {
            string path = Path.Combine(iconsDirectory, fileName);
            byte[] fileData = File.ReadAllBytes(path);
            if (!File.Exists(path))
            {
                ModLog.Log($"Azusa[白洲梓]图标Buff文件未找到: {fileName}");
                return null;
            }
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, fileData))
            {
                ModLog.Log($"Azusa[白洲梓]图片加载失败: {fileName}");
                return null;
            }
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f)
            );
            ModLog.Log($"Azusa[白洲梓]成功加载图标: {fileName}");
            return sprite;
        }
        private Buff GetChargeBuffTemplate(int chargeCount)
        {
            if (chargeCount <= 0 || chargeCount > 3)
                return null;
            if (ChargeBuffTemplate[chargeCount] != null)
                return ChargeBuffTemplate[chargeCount];
            if (buffManager == null)
                buffManager = CharacterMainControl.Main?.GetBuffManager();
            if (buffManager == null)
                return null;
            ModLog.Log($"[Azusa[白洲梓]使用加载的图标，层级: {chargeCount}");
            Sprite icon = ChargeIcon[chargeCount] ?? this.CreateDummySprite(Color.red);
            string nameKey = "";
            string descriptionKey = "";
            switch (chargeCount)
            {
                case 1:
                    nameKey = "毫无意义";
                    descriptionKey = "一切归于无，万事皆徒劳";
                    break;
                case 2:
                    nameKey = "凡事都是虚空";
                    descriptionKey = "那我们为什么还要活着呢……嗯？";
                    break;
                case 3:
                    nameKey = "觉悟吧";
                    descriptionKey = "为了能够不辜负老师的期望。";
                    break;
            }
            ChargeBuffTemplate[chargeCount] = this.CreateBuffTemplate(nameKey, descriptionKey, icon, 149122616);
            return ChargeBuffTemplate[chargeCount];
        }
        private Buff CreateBuffTemplate(string nameKey, string descriptionKey, Sprite icon, int id)
        {
            GameObject gameObject = new GameObject($"ModBuff_Frostcraft_{id}");
            gameObject.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            Buff buffTemplate = gameObject.AddComponent<Buff>();
            Type buffType = typeof(Buff);
            try
            {
                SetFieldSafely(buffTemplate, buffType, "id", id);
                SetFieldSafely(buffTemplate, buffType, "displayName", nameKey);
                SetFieldSafely(buffTemplate, buffType, "description", descriptionKey);
                SetFieldSafely(buffTemplate, buffType, "icon", icon);
                SetFieldSafely(buffTemplate, buffType, "limitedLifeTime", false);
            }
            catch
            {
            }
            return buffTemplate;
        }
        private void SetFieldSafely(object target,Type type , string fieldName, object value)
        {
            try
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                field?.SetValue(target, value);
                ModLog.Log($"✔️设置字段 {fieldName}:{value}");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"❌设置字段 {fieldName} 失败: {ex.Message}");
            }
        }
        private void UpdateCharges_Buff()
        {
            if (!Is_Charging || Current_Charges >= Max_Charges) return;
            Charge_Timer += Time.deltaTime;
            if (Charge_Timer >= Charge_Interval)
            {
                Charge_Timer = 0f;
                Current_Charges++;
                SetChargeGained(Current_Charges);
                if (Current_Charges >= Max_Charges)
                {
                    OnMaxChargesReached();
                }
            }
        }
        private void SetChargeGained(int chargeCount)
        {
            ModLog.Log($"Buff层数: {chargeCount}/{Max_Charges}");
            Item characterItem = CharacterMainControl.Main?.CharacterItem;
            this.RemoveChargeBuff(this.Current_Charges, characterItem);
            this.ApplyChargeBuff(chargeCount, characterItem);
            this.Current_Charges = chargeCount;
        }
        private void OnMaxChargesReached()
        {
            ModLog.Log("Buff达到上限");
            Is_Charging = false;
        }

        private static readonly int MAX_HEALTH = "MaxHealth".GetHashCode();// 最大生命值
        private static readonly int BODY_ARMOR = "BodyArmor".GetHashCode();// 身体护甲
        private static readonly int HEAD_ARMOR = "HeadArmor".GetHashCode();// 头部护甲

        private static readonly int ELEMENT_FACTOR_PHYSICS = "ElementFactor_Physics".GetHashCode();// 物理抵抗
        private static readonly int ELEMENT_FACTOR_FIRE = "ElementFactor_Fire".GetHashCode();// 火焰抵抗
        private static readonly int ELEMENT_FACTOR_POISON = "ElementFactor_Poison".GetHashCode();// 毒素抵抗
        private static readonly int ELEMENT_FACTOR_ELECTRICITY = "ElementFactor_Electricity".GetHashCode();// 电击抗性
        private static readonly int ELEMENT_FACTOR_SPACE = "ElementFactor_Space".GetHashCode();// 空间抗性

        private static readonly int GUN_DAMAGE_MULTIPLIER = "GunDamageMultiplier".GetHashCode();// 枪械伤害乘数
        private static readonly int GUN_CRIT_RATE_GAIN = "GunCritRateGain".GetHashCode();// 枪械暴击率增益
        private static readonly int GUN_CRIT_DAMAGE_GAIN = "GunCritDamageGain".GetHashCode();// 枪械暴击伤害增益 [爆头]

        private void ApplyChargeBuff(int chargeCount, Item PlayerItem)
        {
            if (chargeCount <= 0)
            {
                return;
            }
            switch (chargeCount)
            {
                case 1:
                    PlayerItem.GetStat(GUN_DAMAGE_MULTIPLIER)?.AddModifier(new Modifier((ModifierType)0, 0.2f, chargeSources[chargeCount]));
                    ModLog.Log($"施加 Buff枪械基础伤害提升 +20%");
                    break;
                case 2:
                    PlayerItem.GetStat(GUN_DAMAGE_MULTIPLIER)?.AddModifier(new Modifier((ModifierType)0, 0.24f, chargeSources[chargeCount]));
                    PlayerItem.GetStat(GUN_CRIT_RATE_GAIN)?.AddModifier(new Modifier((ModifierType)0, 0.1f, chargeSources[chargeCount]));
                    ModLog.Log($"施加 Buff枪械基础伤害提升 +24%");
                    ModLog.Log($"施加 Buff枪械暴击概提升 +10%");
                    break;
                case 3:
                    PlayerItem.GetStat(GUN_DAMAGE_MULTIPLIER)?.AddModifier(new Modifier((ModifierType)0, 0.3f, chargeSources[chargeCount]));
                    PlayerItem.GetStat(GUN_CRIT_RATE_GAIN)?.AddModifier(new Modifier((ModifierType)0, 0.15f, chargeSources[chargeCount]));
                    PlayerItem.GetStat(GUN_CRIT_DAMAGE_GAIN)?.AddModifier(new Modifier((ModifierType)0, 0.15f, chargeSources[chargeCount]));
                    ModLog.Log($"施加 Buff枪械基础伤害提升 +30%");
                    ModLog.Log($"施加 Buff枪械暴击概提升 +15%");
                    ModLog.Log($"施加 Buff枪械爆头伤害提升 +15%");
                    break;
            }
            ModLog.Log(chargeSources);
            Buff chargeBuffTemplate = this.GetChargeBuffTemplate(chargeCount);
            buffManager?.AddBuff(chargeBuffTemplate, CharacterMainControl.Main, 0);
        }
        private void RemoveChargeBuff(int chargeCount, Item PlayerItem)
        {
            if (chargeCount <= 0)
                return;
            if (PlayerItem != null)
            {
                PlayerItem.GetStat(GUN_DAMAGE_MULTIPLIER)?.RemoveAllModifiersFromSource(chargeSources[chargeCount]);
                PlayerItem.GetStat(GUN_CRIT_RATE_GAIN)?.RemoveAllModifiersFromSource(chargeSources[chargeCount]);
                PlayerItem.GetStat(GUN_CRIT_DAMAGE_GAIN)?.RemoveAllModifiersFromSource(chargeSources[chargeCount]);

            }
            Buff chargeBuffTemplate = this.GetChargeBuffTemplate(chargeCount);
            if (buffManager != null && chargeBuffTemplate != null)
            {
                buffManager.RemoveBuff(chargeBuffTemplate.ID, false);
            }
        }
        private Sprite CreateDummySprite(Color color)
        {
            const int SIZE = 60;
            const float RADIUS = SIZE / 2f;

            Texture2D texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
            Vector2 center = new Vector2(RADIUS, RADIUS);
            Color[] pixels = new Color[SIZE * SIZE];

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    pixels[y * SIZE + x] = distance > RADIUS - 1.0f ? Color.clear : color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture,
                new Rect(0f, 0f, SIZE, SIZE),
                new Vector2(0.5f, 0.5f)
                );
        }
    }
}
