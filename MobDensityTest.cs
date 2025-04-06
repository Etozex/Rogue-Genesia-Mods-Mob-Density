using ModGenesia;
using RogueGenesia.Data;
using RogueGenesia.GameManager;
using RogueGenesia.Actors.Survival;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using System.Collections;

namespace MobDensityTest
{
    // Custom ability class that overrides the worm spawn count
    public class CustomSummonWormAbility : SummonWormAbility
    {
        // Override to return fixed value regardless of density setting
        public override int GetSpawnedEnemies(BossMonster bossMonster)
        {
            // Get current density factor
            float densityFactor = MobDensityTestMod.GetDensityFactor();
            
            if (!bossMonster.PacifistMode)
            {
                // Counteract the density multiplier
                return Mathf.Max(5, Mathf.RoundToInt(100f / densityFactor));
            }
            return 10; // Original pacifist value
        }
    }

    // Custom ability class that overrides the phase 2 worm spawn count
    public class CustomSummonWormPhase2Ability : SummonWormPhase2Ability
    {
        // Override to return fixed value regardless of density setting
        public override int GetSpawnedEnemies(BossMonster bossMonster)
        {
            // Get current density factor
            float densityFactor = MobDensityTestMod.GetDensityFactor();
            
            if (!bossMonster.PacifistMode)
            {
                // Counteract the density multiplier
                return Mathf.Max(8, Mathf.RoundToInt(150f / densityFactor));
            }
            return 30; // Original pacifist value
        }
    }

    // Harmony patches for the worm boss
    [HarmonyPatch]
    public static class WormBossPatch
    {
        // Patch for the GetSpawnedEnemies method of SummonWormAbility
        [HarmonyPatch(typeof(SummonWormAbility), "GetSpawnedEnemies")]
        [HarmonyPrefix]
        public static bool GetSpawnedEnemiesPrefix(BossMonster bossMonster, ref int __result)
        {
            float densityFactor = MobDensityTestMod.GetDensityFactor();
            
            if (!bossMonster.PacifistMode)
            {
                // Counteract the density by dividing by the density factor
                __result = Mathf.Max(5, Mathf.RoundToInt(100f / densityFactor));
            }
            else
            {
            __result = 10; // Original pacifist value
            }
            return false; // Skip original method
        }

        // Patch for the GetSpawnedEnemies method of SummonWormPhase2Ability
        [HarmonyPatch(typeof(SummonWormPhase2Ability), "GetSpawnedEnemies")]
        [HarmonyPrefix]
        public static bool GetSpawnedEnemiesPhase2Prefix(BossMonster bossMonster, ref int __result)
        {
            float densityFactor = MobDensityTestMod.GetDensityFactor();
            
            if (!bossMonster.PacifistMode)
            {
                // Counteract the density by dividing by the density factor
                __result = Mathf.Max(8, Mathf.RoundToInt(150f / densityFactor));
            }
            else
            {
            __result = 30; // Original pacifist value
            }
            return false; // Skip original method
        }
    }

    // For monitoring of bosses in the scene
    public class BossMonitor : MonoBehaviour
    {
        private BossMonster monitoredBoss;
        private string bossType;

        public void Initialize(BossMonster boss)
        {
            monitoredBoss = boss;
            bossType = boss.GetType().Name;
            
            // Check if this is a worm boss for special handling
            if (boss is WormBoss wormBoss)
            {
                ApplyCustomWormAbilities(wormBoss);
            }
        }

        private void ApplyCustomWormAbilities(WormBoss wormBoss)
        {
            try
            {
                // Replace phase 1 ability if not already custom
                if (wormBoss.SummonWormAbility != null && !(wormBoss.SummonWormAbility is CustomSummonWormAbility))
                {
                    wormBoss.SummonWormAbility = MobDensityTestMod.GetCustomWormAbility();
                }
                
                // Replace phase 2 ability if not already custom
                if (wormBoss.SummonWormPhase2Ability != null && !(wormBoss.SummonWormPhase2Ability is CustomSummonWormPhase2Ability))
                {
                    wormBoss.SummonWormPhase2Ability = MobDensityTestMod.GetCustomWormPhase2Ability();
                }
            }
            catch (Exception) { /* Silent fail, not critical */ }
        }

        void Update()
        {
            // If boss is destroyed, remove this component
            if (monitoredBoss == null)
            {
                Destroy(this);
            }
        }
    }

    public class MobDensityTestMod : RogueGenesiaMod
    {
        private static string logFilePath;
        private static float densityFactor = 1.0f;
        
        // Public constant for logging/reference
        public const string MOD_ID = "MobDensityTest";
        
        private const string DENSITY_OPTION = "monster_density";
        private const string PREFS_KEY = "MobDensityTest_DensityValue";
        private const float DEFAULT_DENSITY = 1.0f;
        
        // Store original EnemyCount value
        private static float originalEnemyCount = 1.0f;
        private static bool initializedOriginalValue = false;
        
        // Custom ability instances
        private static readonly CustomSummonWormAbility customSummonWormAbility = new CustomSummonWormAbility();
        private static readonly CustomSummonWormPhase2Ability customSummonWormPhase2Ability = new CustomSummonWormPhase2Ability();

        // Harmony instance
        private Harmony harmony;

        // Patterns to scale down proportionally to density
        private readonly HashSet<PatternMonsterType> patternsToScale = new HashSet<PatternMonsterType>
        {
            PatternMonsterType.Circle,
            PatternMonsterType.ImmobileCircle,
            PatternMonsterType.Orbiting,
            PatternMonsterType.MonsterWave,
            PatternMonsterType.BigMonsterWave
        };

        // Dictionary to store original pattern values
        private Dictionary<string, PatternOriginalValues> originalPatternValues = new Dictionary<string, PatternOriginalValues>();

        // Class to store original pattern values
        private class PatternOriginalValues
        {
            public int MinCount { get; set; }
            public int MaxCount { get; set; }
            public bool Initialized { get; set; }
        }

        // Public method to get the current density factor (for Harmony patches)
        public static float GetDensityFactor()
        {
            return densityFactor;
        }
        
        // Provide access to custom abilities
        public static CustomSummonWormAbility GetCustomWormAbility()
        {
            return customSummonWormAbility;
        }
        
        public static CustomSummonWormPhase2Ability GetCustomWormPhase2Ability()
        {
            return customSummonWormPhase2Ability;
        }

        public override void OnModLoaded(ModData modData)
        {
            string persistentDataPath = Application.persistentDataPath;
            logFilePath = Path.Combine(persistentDataPath, "MobDensityTest_log.txt");
            
            // Initialize log file
            File.WriteAllText(logFilePath, $"=== MobDensityTest Log Started {DateTime.Now} ===\n");
            
            // Load saved density value from PlayerPrefs
            LoadSettings();

            // Apply Harmony patches
            ApplyHarmonyPatches();

            // Create the density slider option
            AddDensitySlider();

            // Register event listeners
            RegisterEventHandlers();
        }
        
        private void ApplyHarmonyPatches()
        {
            harmony = new Harmony("com.mesos.mobdensitytest");
            try
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{MOD_ID}] Error applying Harmony patches: {ex.Message}");
            }
        }
        
        private void AddDensitySlider()
        {
            var densityNameLocalization = new LocalizationDataList
            {
                localization = new List<LocalizationData>()
            {
                new LocalizationData() { Key = "en", Value = "Monster Density" }
                }
            };

            var densityTooltipLocalization = new LocalizationDataList
            {
                localization = new List<LocalizationData>()
            {
                new LocalizationData() { Key = "en", Value = "Adjust monster density (1.0 = normal, 2.0 = double, up to 20x). Pattern spawns (circles, waves) and boss summons will not be affected." }
                }
            };

            var densityOption = ModOption.MakeSliderDisplayValueOption(
                DENSITY_OPTION,
                densityNameLocalization,
                1.0f,   // Min value
                20.0f,  // Max value
                densityFactor,   // Use loaded value
                38,     // Steps
                false,  // Display as percentage
                densityTooltipLocalization
            );

            ModOption.AddModOption(densityOption, "Gameplay Options", "Monster Density");
        }

        private void RegisterEventHandlers()
        {
            GameEventManager.OnGameStart.AddListener(OnGameStart);
            GameEventManager.OnStageStart.AddListener(OnStageStart);
            GameEventManager.OnOptionConfirmed.AddListener(OnOptionChanged);
            GameEventManager.OnCalculateMonsterStats.AddListener(OnCalculateMonsterStats);
            GameEventManager.OnPostStatsUpdate.AddListener(OnPostStatsUpdate);
            GameEventManager.OnRunLoad.AddListener(OnRunLoad);
        }

        private void LoadSettings()
        {
            try
            {
                if (PlayerPrefs.HasKey(PREFS_KEY))
                {
                    densityFactor = PlayerPrefs.GetFloat(PREFS_KEY);
                }
                else
                {
                    densityFactor = DEFAULT_DENSITY;
                }
            }
            catch
            {
                densityFactor = DEFAULT_DENSITY;
            }
        }

        private void SaveSettings()
        {
            try
            {
                PlayerPrefs.SetFloat(PREFS_KEY, densityFactor);
                PlayerPrefs.Save();
            }
            catch (Exception) { /* Silent fail, not critical */ }
        }

        // Called when player stats are recalculated
        private void OnPostStatsUpdate(AvatarData avatarData, PlayerStats playerStats, bool isBaseStats)
        {
            if (playerStats != null)
            {
                // Store original value if we haven't yet
                if (!initializedOriginalValue && playerStats.EnemyCount != null)
                {
                    originalEnemyCount = (float)playerStats.EnemyCount.GetDefaultValue();
                    initializedOriginalValue = true;
                }
                
                // Set the density multiplier
                if (playerStats.EnemyCount != null)
                {
                float newValue = originalEnemyCount * densityFactor;
                    playerStats.EnemyCount.SetDefaultBaseStat((double)newValue);
                }
                
                // Adjust excluded patterns to counter density effect
                AdjustPatternEnemies();
            }
        }
        
        // Check each monster when its stats are calculated
        private void OnCalculateMonsterStats(Monster monster)
        {
            if (monster == null) return;
            
            // If it's a boss, attach our monitor component
            if (monster is BossMonster bossMonster)
            {
                // Only create a monitor if it doesn't already have one
                BossMonitor existingMonitor = monster.gameObject.GetComponent<BossMonitor>();
                if (existingMonitor == null)
                {
                    try
                    {
                        BossMonitor monitor = monster.gameObject.AddComponent<BossMonitor>();
                        monitor.Initialize(bossMonster);
                    }
                    catch (Exception) { /* Silent fail, not critical */ }
                }
                
                // Special handling for worm boss
                if (monster is WormBoss wormBoss)
                {
                    ApplyWormBossCustomAbilities(wormBoss);
                                }
                                else
                                {
                    // For any other boss type, try to limit spawn counts
                    LimitBossSummonCounts(bossMonster);
                }
            }
        }
        
        private void ApplyWormBossCustomAbilities(WormBoss wormBoss)
        {
            try
            {
                // Replace phase 1 ability if not already custom
                if (wormBoss.SummonWormAbility != null && !(wormBoss.SummonWormAbility is CustomSummonWormAbility))
                {
                    wormBoss.SummonWormAbility = customSummonWormAbility;
                }
                
                // Replace phase 2 ability if not already custom
                if (wormBoss.SummonWormPhase2Ability != null && !(wormBoss.SummonWormPhase2Ability is CustomSummonWormPhase2Ability))
                {
                    wormBoss.SummonWormPhase2Ability = customSummonWormPhase2Ability;
                }
                
                // Also try to limit any spawn count fields
                        LimitBossSummonCounts(wormBoss);
                    }
            catch (Exception) { /* Silent fail, not critical */ }
        }
        
        // Try to find and limit any spawn count fields in boss objects
        private void LimitBossSummonCounts(BossMonster bossMonster)
        {
            if (bossMonster == null) return;
            
            try
            {
                Type bossType = bossMonster.GetType();
                
                // Look through all fields
                var fields = bossType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                foreach (var field in fields)
                {
                    // Look for fields that might control spawning
                    if ((field.Name.ToLowerInvariant().Contains("spawn") || 
                        field.Name.ToLowerInvariant().Contains("summon") ||
                        field.Name.ToLowerInvariant().Contains("count")) && 
                        field.FieldType == typeof(int))
                        {
                            try
                            {
                                int originalValue = (int)field.GetValue(bossMonster);
                                
                            // Only limit if it's a significant number
                            if (originalValue > 10)
                                {
                                    int limitedValue = Math.Min(originalValue, 100); // Cap at 100
                                    field.SetValue(bossMonster, limitedValue);
                            }
                        }
                        catch (Exception) { /* Silent fail, not critical */ }
                    }
                }
            }
            catch (Exception) { /* Silent fail, not critical */ }
        }
        
        // Adjust pattern enemy counts to counteract density changes
        private void AdjustPatternEnemies()
        {
            Debug.Log($"[{MOD_ID}] AdjustPatternEnemies called with density factor: {densityFactor}");
            
            try
            {
                // Get all pattern scriptable objects
                var patterns = GameDataGetter.GetAllScriptableObjectOfType<EnemyPatternScriptableObject>();
                if (patterns == null || patterns.Length == 0) return;
                
                foreach (var pattern in patterns)
                {
                    if (pattern == null) continue;
                    
                    // Only scale down the specific patterns we want to adjust
                    if (patternsToScale.Contains(pattern.patternMonster))
                    {
                        ApplyScalingToPattern(pattern);
                    }
                    
                    // Always try to adjust spawn distance for safety
                    TryIncreaseSpawnDistance(pattern);
                }
            }
            catch (Exception ex) 
            { 
                Debug.LogError($"[{MOD_ID}] Error in AdjustPatternEnemies: {ex.Message}");
            }
        }
        
        // Apply scaling to a specific pattern
        private void ApplyScalingToPattern(EnemyPatternScriptableObject pattern)
        {
            string patternId = GetPatternUniqueId(pattern);
            
            try
            {
                // If we haven't stored the original values yet
                if (!originalPatternValues.TryGetValue(patternId, out PatternOriginalValues original))
                {
                    // Store original values
                    original = new PatternOriginalValues
                    {
                        MinCount = Math.Max(1, pattern.minMonsterCount),
                        MaxCount = Math.Max(1, pattern.maxMonsterCount),
                        Initialized = true
                    };
                    
                    originalPatternValues[patternId] = original;
                    
                    Debug.Log($"[{MOD_ID}] Stored original values for pattern {pattern.name}: {original.MinCount}-{original.MaxCount}");
                }
                
                if (original.Initialized && original.MinCount > 0 && original.MaxCount > 0)
                {
                    // *** IMPORTANT CHANGE: ALWAYS DIVIDE BY DENSITY FACTOR ***
                    // This ensures pattern counts are always REDUCED proportionally to density
                    // so at 20x density, pattern will have 1/20th the normal enemies
                        float inverseFactor = 1.0f / densityFactor;
                    
                    // Calculate new values - divide by density factor
                    int newMin = Math.Max(1, Mathf.RoundToInt(original.MinCount * inverseFactor));
                    int newMax = Math.Max(newMin, Mathf.RoundToInt(original.MaxCount * inverseFactor));
                    
                    // Keep original values if they were already small
                    if (original.MinCount <= 3)
                    {
                        newMin = Math.Max(1, Mathf.RoundToInt(original.MinCount * inverseFactor));
                    }
                    
                    if (original.MaxCount <= 5)
                    {
                        newMax = Math.Max(newMin, Mathf.RoundToInt(original.MaxCount * inverseFactor));
                    }
                    
                    // Apply the scaled values - never go below 1
                    pattern.minMonsterCount = Math.Max(1, newMin);
                    pattern.maxMonsterCount = Math.Max(pattern.minMonsterCount, newMax);
                    
                    Debug.Log($"[{MOD_ID}] REDUCED pattern {pattern.name} ({pattern.patternMonster}) from {original.MinCount}-{original.MaxCount} to {pattern.minMonsterCount}-{pattern.maxMonsterCount} (divided by {densityFactor})");
                                }
                                else
                                {
                    // Ensure minimum values for any pattern
                    if (pattern.minMonsterCount < 1) pattern.minMonsterCount = 1;
                    if (pattern.maxMonsterCount < pattern.minMonsterCount) pattern.maxMonsterCount = pattern.minMonsterCount;
                    
                    Debug.Log($"[{MOD_ID}] Ensured minimum values for pattern {pattern.name}: {pattern.minMonsterCount}-{pattern.maxMonsterCount}");
                                }
                            }
                            catch (Exception ex)
                            {
                Debug.LogError($"[{MOD_ID}] Error scaling pattern {pattern.name}: {ex.Message}");
                
                // Emergency recovery - set some safe values
                if (pattern.minMonsterCount < 1) pattern.minMonsterCount = 1;
                if (pattern.maxMonsterCount < 1) pattern.maxMonsterCount = 3;
            }
        }
        
        // Get a unique ID for a pattern to use as dictionary key
        private string GetPatternUniqueId(EnemyPatternScriptableObject pattern)
        {
            return $"{pattern.name}_{pattern.patternMonster}";
        }

        private void OnOptionChanged(string optionName, float newValue)
        {
            if (optionName == DENSITY_OPTION)
            {
                densityFactor = newValue;
                
                // Save the value immediately
                SaveSettings();
                
                // Apply the new density setting
                ApplyDensitySetting();
                
                // Update pattern enemies to counteract density
                AdjustPatternEnemies();
                
                Debug.Log($"[{MOD_ID}] Density changed to {densityFactor}x - Applied to player stats and patterns");
            }
        }

        private void OnGameStart()
        {
            Debug.Log($"[{MOD_ID}] OnGameStart - Applying density setting {densityFactor}x");
            ApplyDensitySetting();
            AdjustPatternEnemies();
        }
        
        private void OnRunLoad()
        {
            Debug.Log($"[{MOD_ID}] OnRunLoad - Reapplying density setting {densityFactor}x");
            ApplyDensitySetting();
            AdjustPatternEnemies();
        }

        private void OnStageStart(LevelObject levelObject)
        {
            Debug.Log($"[{MOD_ID}] OnStageStart - Reapplying density setting {densityFactor}x");
            
            // Re-apply enemy count at stage start (may get reset)
            ApplyDensitySetting();
            
            // Adjust pattern enemies again
            AdjustPatternEnemies();
            
            // Check for bosses in the scene
            ScanForBossesInScene();
        }
        
        private void ApplyDensitySetting()
        {
            if (GameData.PlayerDatabase == null || GameData.PlayerDatabase.Count <= 0) return;
            
            var player = GameData.PlayerDatabase[0];
            if (player == null) return;
            
            // Initialize original value if needed
            if (!initializedOriginalValue && player._playerStats?.EnemyCount != null)
            {
                originalEnemyCount = (float)player._playerStats.EnemyCount.GetDefaultValue();
                        initializedOriginalValue = true;
            }
            
            // Calculate effective density with scaling to leave room for patterns
            float effectiveDensity = densityFactor;
            if (densityFactor > 3f)
            {
                // Scale down regular enemies as density increases to leave space for patterns
                // At max density (20), we'll reduce by 25% to leave room for patterns
                float patternReservation = Mathf.Lerp(0f, 0.25f, (densityFactor - 3f) / 17f);
                effectiveDensity = densityFactor * (1f - patternReservation);
                Debug.Log($"[{MOD_ID}] Density {densityFactor}x scaled to {effectiveDensity:F2}x to reserve {patternReservation*100:F0}% for patterns");
                    }
                    
                    // Apply to base stats
            if (player._basePlayerStats?.EnemyCount != null)
                    {
                float newValue = originalEnemyCount * effectiveDensity;
                player._basePlayerStats.EnemyCount.SetDefaultBaseStat((double)newValue);
                    }
                    
                    // Apply to current stats
            if (player._playerStats?.EnemyCount != null)
            {
                float newValue = originalEnemyCount * effectiveDensity;
                player._playerStats.EnemyCount.SetDefaultBaseStat((double)newValue);
            }
        }
        
        private void ScanForBossesInScene()
        {
            try
            {
                // We can't directly use FindObjectsOfType<Monster>, need to scan GameObjects
                var allGameObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                
                foreach (var go in allGameObjects)
                {
                    // Skip inactive game objects
                    if (!go.activeInHierarchy) continue;
                    
                    // Try to get a monster component
                    var monsterComponent = go.GetComponent<Monster>();
                    if (monsterComponent != null && 
                       (monsterComponent is BossMonster || monsterComponent is WormBoss || 
                        go.name.Contains("Boss") || go.name.Contains("Worm")))
                    {
                        // Process this boss monster
                        OnCalculateMonsterStats(monsterComponent);
                    }
                }
            }
            catch (Exception) { /* Silent fail, not critical */ }
        }

        // Reset pattern values when unloading the mod
        private void ResetPatternValues()
        {
            try
            {
                Debug.Log($"[{MOD_ID}] Resetting all pattern values to original");
                
                var patterns = GameDataGetter.GetAllScriptableObjectOfType<EnemyPatternScriptableObject>();
                if (patterns == null || patterns.Length == 0) return;
                
                foreach (var pattern in patterns)
                {
                    if (pattern == null) continue;
                    
                    string patternId = GetPatternUniqueId(pattern);
                    
                    if (originalPatternValues.TryGetValue(patternId, out PatternOriginalValues original) && original.Initialized)
                    {
                        // Restore original values
                        pattern.minMonsterCount = original.MinCount;
                        pattern.maxMonsterCount = original.MaxCount;
                        
                        Debug.Log($"[{MOD_ID}] Reset pattern {pattern.name} to original values: {original.MinCount}-{original.MaxCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{MOD_ID}] Error resetting pattern values: {ex.Message}");
            }
        }

        public static void LogToFile(string message)
        {
            try
            {
                if (File.Exists(logFilePath))
                {
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
                }
            }
            catch (Exception) { /* Silent fail, not critical */ }
        }

        public static void LogImportant(string message)
        {
            try
            {
                if (File.Exists(logFilePath))
                {
                    string highlighted = new string('*', 80) + "\n" +
                                         "!!! " + message + " !!!\n" + 
                                         new string('*', 80);
                    
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {highlighted}\n");
                    Debug.LogWarning($"[MobDensityTest] {message}"); // Also output to Unity console
                }
            }
            catch (Exception) { /* Silent fail, not critical */ }
        }

        public override void OnModUnloaded()
        {
            // Reset pattern values to original
            ResetPatternValues();
            
            // Remove Harmony patches
            if (harmony != null)
            {
                try
                {
                    harmony.UnpatchAll(harmony.Id);
                }
                catch (Exception) { /* Silent fail, not critical */ }
            }
            
            // Unhook events
            GameEventManager.OnGameStart.RemoveListener(OnGameStart);
            GameEventManager.OnStageStart.RemoveListener(OnStageStart);
            GameEventManager.OnOptionConfirmed.RemoveListener(OnOptionChanged);
            GameEventManager.OnCalculateMonsterStats.RemoveListener(OnCalculateMonsterStats);
            GameEventManager.OnPostStatsUpdate.RemoveListener(OnPostStatsUpdate);
            GameEventManager.OnRunLoad.RemoveListener(OnRunLoad);
            
            // Reset EnemyCount multiplier if exists
            if (GameData.PlayerDatabase != null && GameData.PlayerDatabase.Count > 0 && 
                GameData.PlayerDatabase[0] != null && GameData.PlayerDatabase[0]._playerStats?.EnemyCount != null)
            {
                // Reset to original value
                GameData.PlayerDatabase[0]._playerStats.EnemyCount.SetDefaultBaseStat((double)originalEnemyCount);
            }
        }

        // Try to adjust spawn distance fields using reflection
        private void TryIncreaseSpawnDistance(EnemyPatternScriptableObject pattern)
        {
            try
            {
                Type patternType = pattern.GetType();
                
                // Get all fields that might control spawn distance
                var fields = patternType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                foreach (var field in fields)
                {
                    string fieldName = field.Name.ToLowerInvariant();
                    
                    // Look for fields that might be related to distance/spawn position
                    if (fieldName.Contains("distance") || fieldName.Contains("radius") || 
                        fieldName.Contains("range") || fieldName.Contains("spawn"))
                    {
                        if (field.FieldType == typeof(float))
                        {
                            // For radius/range fields, increase them to create more space
                            float currentValue = (float)field.GetValue(pattern);
                            
                            if (fieldName.Contains("min"))
                            {
                                // Use more conservative safe value - don't increase too much
                                float safeValue = Math.Max(10f, currentValue);
                                field.SetValue(pattern, safeValue);
                            }
                            else if (fieldName.Contains("max"))
                            {
                                // Use more conservative maximum - don't push enemies too far
                                float safeValue = Math.Max(10f, currentValue);
                                field.SetValue(pattern, safeValue);
                            }
                        }
                    }
                }
                
                // Try accessing radius field which is commonly used for many pattern types
                var radiusField = patternType.GetField("radius", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (radiusField != null && radiusField.FieldType == typeof(float))
                {
                    float radius = (float)radiusField.GetValue(pattern);
                    // Ensure radius is reasonable but don't make it too large
                    if (radius < 1f) 
                    {
                        radiusField.SetValue(pattern, 10f);
                    }
                }
            }
            catch (Exception) { /* Silent fail, not critical */ }
        }
    }

    // Harmony patches for enemy spawning
    [HarmonyPatch]
    public static class EnemySpawnPatch
    {
        // Minimum safe distance from player for any enemy spawn
        private const float MIN_SAFE_DISTANCE = 8f;
        
        // Patch for individual enemy spawning - prefix to prevent initial spawn on player
        [HarmonyPatch(typeof(EnemyManager), "SpawnEnemy", new Type[] { 
            typeof(PlayerEntity), typeof(GameObject), typeof(Vector3), 
            typeof(float), typeof(EnemySO), typeof(float), typeof(bool), 
            typeof(bool), typeof(bool), typeof(EEnemyTier), typeof(EnemyAISO) })]
        [HarmonyPrefix]
        public static void SpawnEnemyPrefix(ref Vector3 posToSpawn, PlayerEntity player)
        {
            try
            {
                if (player == null) return;
                
                // Get player position (ignoring y-axis for 2D gameplay)
                Vector3 playerPos = player.transform.position;
                Vector2 playerPos2D = new Vector2(playerPos.x, playerPos.z);
                Vector2 spawnPos2D = new Vector2(posToSpawn.x, posToSpawn.z);
                
                // Calculate distance to player (in 2D plane)
                float distanceToPlayer = Vector2.Distance(spawnPos2D, playerPos2D);
                
                // If too close to player, push away to minimum safe distance
                if (distanceToPlayer < MIN_SAFE_DISTANCE)
                {
                    // Get direction from player to spawn point
                    Vector2 dirFromPlayer = (distanceToPlayer > 0.1f) 
                        ? (spawnPos2D - playerPos2D).normalized 
                        : UnityEngine.Random.insideUnitCircle.normalized; // Random direction if on player
                    
                    // Set new position at safe distance
                    Vector2 newPos2D = playerPos2D + dirFromPlayer * MIN_SAFE_DISTANCE;
                    
                    // Update the spawn position (keeping original Y value)
                    posToSpawn = new Vector3(newPos2D.x, posToSpawn.y, newPos2D.y);
                    
                    Debug.Log($"[{MobDensityTestMod.MOD_ID}] Prevented enemy from spawning on player - moved from distance {distanceToPlayer:F1} to {MIN_SAFE_DISTANCE}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{MobDensityTestMod.MOD_ID}] Error in SpawnEnemyPrefix: {ex.Message}");
            }
        }
        
        // Postfix patch to catch monsters after they're spawned and move them if needed
        [HarmonyPatch(typeof(EnemyManager), "SpawnEnemy", new Type[] { 
            typeof(PlayerEntity), typeof(GameObject), typeof(Vector3), 
            typeof(float), typeof(EnemySO), typeof(float), typeof(bool), 
            typeof(bool), typeof(bool), typeof(EEnemyTier), typeof(EnemyAISO) })]
        [HarmonyPostfix]
        public static void SpawnEnemyPostfix(ref Monster __result, PlayerEntity player)
        {
            try
            {
                if (__result == null || player == null) return;
                
                // Apply safe distance to the newly spawned monster
                MoveMonsterIfTooClose(__result, player);
                }
                catch (Exception ex)
                {
                Debug.LogError($"[{MobDensityTestMod.MOD_ID}] Error in SpawnEnemyPostfix: {ex.Message}");
            }
        }
        
        // Handle pattern spawning by moving all enemies after pattern creation
        [HarmonyPatch(typeof(EnemyManager), "SpawnEnemyPattern")]
        [HarmonyPostfix]
        public static void SpawnEnemyPatternPostfix(PlayerEntity player)
        {
            try
            {
                if (player == null) return;
                
                // Find all monsters in the scene and make sure they respect minimum distance
                StartCoroutine(DelayedMoveAllMonstersIfTooClose(player));
                }
                catch (Exception ex)
                {
                Debug.LogError($"[{MobDensityTestMod.MOD_ID}] Error in SpawnEnemyPatternPostfix: {ex.Message}");
            }
        }
        
        // Helper to start a coroutine from static context
        private static void StartCoroutine(IEnumerator routine)
        {
            if (GameData.PlayerDatabase != null && GameData.PlayerDatabase.Count > 0 && 
                GameData.PlayerDatabase[0]?.PlayerEntity != null)
            {
                GameData.PlayerDatabase[0].PlayerEntity.StartCoroutine(routine);
            }
        }
        
        // Helper method to move a single monster if it's too close to player
        private static void MoveMonsterIfTooClose(Monster monster, PlayerEntity player)
        {
            if (monster == null || player == null) return;
            
            // Get the monster's position - use GetPosition utility method that works with new Monster class
            Vector3 monsterPos = GetMonsterPosition(monster);
            Vector3 playerPos = player.transform.position;
            
            // Calculate 2D distance (ignoring Y axis)
            Vector2 monsterPos2D = new Vector2(monsterPos.x, monsterPos.z);
            Vector2 playerPos2D = new Vector2(playerPos.x, playerPos.z);
            float distanceToPlayer = Vector2.Distance(monsterPos2D, playerPos2D);
            
            // If monster is too close to player, move it away
            if (distanceToPlayer < MIN_SAFE_DISTANCE)
            {
                // Get direction from player to monster
                Vector2 dirFromPlayer = (distanceToPlayer > 0.1f) 
                    ? (monsterPos2D - playerPos2D).normalized 
                    : UnityEngine.Random.insideUnitCircle.normalized;
                
                // Calculate new position at safe distance
                Vector2 newPos2D = playerPos2D + dirFromPlayer * MIN_SAFE_DISTANCE;
                
                // Update monster position
                SetMonsterPosition(monster, new Vector3(newPos2D.x, monsterPos.y, newPos2D.y));
                
                Debug.Log($"[{MobDensityTestMod.MOD_ID}] Moved monster away from player - dist: {distanceToPlayer:F1} to {MIN_SAFE_DISTANCE}");
            }
        }
        
        // Delayed check for all monsters - runs after pattern spawn completes
        private static IEnumerator DelayedMoveAllMonstersIfTooClose(PlayerEntity player)
        {
            // Wait a frame for all pattern monsters to be fully spawned and positioned
            yield return null;
            yield return null; // Wait 2 frames to be sure
            
            try
            {
                // Find all monsters - use GameObject.FindObjectsOfType<GameObject> and filter
                List<Monster> allMonsters = FindAllMonsters();
                int movedCount = 0;
                
                foreach (Monster monster in allMonsters)
                {
                    if (monster == null) continue;
                    
                    // Get the monster's position - use GetPosition utility method
                    Vector3 monsterPos = GetMonsterPosition(monster);
                    Vector3 playerPos = player.transform.position;
                    
                    // Calculate 2D distance (ignoring Y axis)
                    Vector2 monsterPos2D = new Vector2(monsterPos.x, monsterPos.z);
                    Vector2 playerPos2D = new Vector2(playerPos.x, playerPos.z);
                    float distanceToPlayer = Vector2.Distance(monsterPos2D, playerPos2D);
                    
                    // If monster is too close to player, move it away
                    if (distanceToPlayer < MIN_SAFE_DISTANCE)
                    {
                        // Get direction from player to monster
                        Vector2 dirFromPlayer = (distanceToPlayer > 0.1f) 
                            ? (monsterPos2D - playerPos2D).normalized 
                            : UnityEngine.Random.insideUnitCircle.normalized;
                        
                        // Calculate new position at safe distance
                        Vector2 newPos2D = playerPos2D + dirFromPlayer * MIN_SAFE_DISTANCE;
                        
                        // Update monster position
                        SetMonsterPosition(monster, new Vector3(newPos2D.x, monsterPos.y, newPos2D.y));
                        movedCount++;
                    }
                }
                
                if (movedCount > 0)
                {
                    Debug.Log($"[{MobDensityTestMod.MOD_ID}] Moved {movedCount} pattern enemies away from player after spawn");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{MobDensityTestMod.MOD_ID}] Error in delayed monster check: {ex.Message}");
            }
        }

        // Helper methods for getting/setting monster positions that work with the new Monster implementation
        private static Vector3 GetMonsterPosition(Monster monster)
        {
            try
            {
                // Try to get position via GameObject property first
                if (monster.gameObject != null)
                {
                    return monster.gameObject.transform.position;
                }
                
                // Try direct access to a Position property if it exists
                var posProperty = monster.GetType().GetProperty("Position");
                if (posProperty != null)
                {
                    return (Vector3)posProperty.GetValue(monster);
                }
                
                // Try to access a transform component or field
                var transformField = monster.GetType().GetField("transform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (transformField != null)
                {
                    var transform = transformField.GetValue(monster) as Transform;
                    if (transform != null)
                    {
                        return transform.position;
                    }
                }
                
                // Fall back to Vector3.zero if we can't get the position
                Debug.LogWarning($"[{MobDensityTestMod.MOD_ID}] Could not get position for monster {monster}");
                return Vector3.zero;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{MobDensityTestMod.MOD_ID}] Error getting monster position: {ex.Message}");
                return Vector3.zero;
            }
        }
        
        private static void SetMonsterPosition(Monster monster, Vector3 position)
        {
            try
            {
                // Try to set position via GameObject property first
                if (monster.gameObject != null)
                {
                    monster.gameObject.transform.position = position;
                    return;
                }
                
                // Try direct access to a Position property if it exists
                var posProperty = monster.GetType().GetProperty("Position");
                if (posProperty != null)
                {
                    posProperty.SetValue(monster, position);
                    return;
                }
                
                // Try to access a transform component or field
                var transformField = monster.GetType().GetField("transform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (transformField != null)
                {
                    var transform = transformField.GetValue(monster) as Transform;
                    if (transform != null)
                    {
                        transform.position = position;
                        return;
                    }
                }
                
                Debug.LogWarning($"[{MobDensityTestMod.MOD_ID}] Could not set position for monster {monster}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{MobDensityTestMod.MOD_ID}] Error setting monster position: {ex.Message}");
            }
        }
        
        // Helper method to find all monsters in the scene
        private static List<Monster> FindAllMonsters()
        {
            List<Monster> monsters = new List<Monster>();
            
            try
            {
                // First try to find gameobjects with Monster component
                foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                {
                    // Try to get a Monster component
                    Monster monster = null;
                    
                    try
                    {
                        monster = go.GetComponent<Monster>();
                    }
                    catch { /* Ignore errors */ }
                    
                    if (monster != null)
                    {
                        monsters.Add(monster);
                    }
                }
                
                if (monsters.Count == 0)
                {
                    // Alternative approach: try to use EnemyManager to get monsters
                    // This is just a placeholder - you'd need to implement based on the actual API
                    Debug.LogWarning($"[{MobDensityTestMod.MOD_ID}] Could not find monsters using GameObject.FindObjectsOfType");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{MobDensityTestMod.MOD_ID}] Error finding monsters: {ex.Message}");
            }
            
            return monsters;
        }
    }
}