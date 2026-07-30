using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BackShieldMod
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class BackShieldPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.oma.backshield";
        public const string PluginName = "BackShieldMod";
        public const string PluginVersion = "1.0.0";

        private Harmony harmony;

        // ingame settings
        public static ConfigEntry<float> RoundShieldEfficiency;
        public static ConfigEntry<float> TowerShieldEfficiency;

        private void Awake()
        {
            // create settings that can be adjusted ingame or from the .cfg file
            RoundShieldEfficiency = Config.Bind(
                "Efficiency",                        // teho
                "RoundShieldEfficiency",             // setting name
                0.85f,                               // default value (85%)
                new ConfigDescription("Pyöreiden kilpien ja bucklerien torjuntateho selässä (0.0 = 0%, 1.0 = 100%)", new AcceptableValueRange<float>(0f, 1f))
            );

            TowerShieldEfficiency = Config.Bind(
                "Efficiency",                        // teho
                "TowerShieldEfficiency",             // setting name
                0.65f,                               // default value (65%)
                new ConfigDescription("Tornikilpien torjuntateho selässä (0.0 = 0%, 1.0 = 100%)", new AcceptableValueRange<float>(0f, 1f))
            );

            harmony = new Harmony(PluginGUID);
            harmony.PatchAll();

        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    public static class ApplyDamage_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Character __instance, HitData hit)
        {
            if (!(__instance is Player player)) return;

            float totalDamage = hit.GetTotalDamage();
            if (totalDamage <= 0f) return;

            // reverse the hit direction to point towards the attacker
            Vector3 attackerDirection = -hit.m_dir;
            
            // calculate the angle relative to the back
            float angleFromBack = Vector3.Angle(-player.transform.forward, attackerDirection);

            // if the hit comes from the back (less than 60 degrees from the back centerline)
            if (angleFromBack < 60f)
            {
                float hitHeight = hit.m_point.y - player.transform.position.y;
                bool hitBackArea = hitHeight >= 0.5f && hitHeight <= 2.0f;

                if (!hitBackArea) return;

                // get the shield on the back (m_hiddenLeftItem)
                ItemDrop.ItemData backShield = AccessTools.Field(typeof(Humanoid), "m_hiddenLeftItem")?.GetValue(player) as ItemDrop.ItemData;

                if (backShield != null && backShield.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
                {
                    // calculate the character's blocking skill (0.0 - 1.0)
                    float skillFactor = player.GetSkillFactor(Skills.SkillType.Blocking);

                    // calculate the shield's raw block power
                    float rawBlockPower = backShield.GetBlockPower(skillFactor);

                    // identify the shield type and read the value directly from the CONFIG (.Value)
                    bool isTowerShield = backShield.m_shared.m_timedBlockBonus <= 1.0f;
                    float efficiency = isTowerShield 
                        ? BackShieldPlugin.TowerShieldEfficiency.Value 
                        : BackShieldPlugin.RoundShieldEfficiency.Value;

                    // calculate the modified block power for the back
                    float blockPower = rawBlockPower * efficiency;

                    // stamina consumption
                    float staminaCost = 10f;
                    if (player.HaveStamina(staminaCost))
                    {
                        player.UseStamina(staminaCost);
                    }

                    // damage after shield
                    float damageAfterShield = Mathf.Max(0f, totalDamage - blockPower);

                    // apply body armor
                    float bodyArmor = player.GetBodyArmor();
                    float finalDamage = 0f;

                    if (damageAfterShield > 0f)
                    {
                        if (damageAfterShield < bodyArmor * 0.5f)
                        {
                            finalDamage = damageAfterShield * damageAfterShield / (bodyArmor * 2f);
                        }
                        else
                        {
                            finalDamage = damageAfterShield - bodyArmor * 0.5f;
                        }
                        finalDamage = Mathf.Max(0f, finalDamage);
                    }

                    // modify damage to HitData object
                    hit.ApplyModifier(finalDamage / totalDamage);

                    // visuals and text
                    if (backShield.m_shared.m_blockEffect != null)
                    {
                        backShield.m_shared.m_blockEffect.Create(hit.m_point, Quaternion.identity);
                    }

                    if (DamageText.instance != null)
                    {
                        DamageText.instance.ShowText(
                            DamageText.TextType.Blocked,
                            hit.m_point,
                            finalDamage
                        );
                    }

                    // blocking-skill leveling
                    player.RaiseSkill(Skills.SkillType.Blocking, 0.5f);

                }
            }
        }
    }
}