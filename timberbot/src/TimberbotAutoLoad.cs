// TimberbotAutoLoad.cs -- Auto-load a save from CLI args at the main menu.
//
// Checks System.Environment.GetCommandLineArgs() for:
//   --tb-settlement <name>   (required to activate auto-load)
//   --tb-save <filename>     (optional, defaults to most recent save in settlement)
//
// Uses ValidatingGameLoader.LoadGame() -- same path as clicking Continue/Load in the UI.
// Runs as ILoadableSingleton in [Context("MainMenu")] so it fires once when the menu loads.

using System;
using System.IO;
using System.Linq;
using Timberborn.GameSaveRepositorySystem;
using Timberborn.GameSaveRepositorySystemUI;
using Timberborn.PlatformUtilities;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Timberbot
{
    public class TimberbotAutoLoad : ILoadableSingleton
    {
        private readonly GameSaveRepository _gameSaveRepository;
        private readonly ValidatingGameLoader _validatingGameLoader;

        public TimberbotAutoLoad(
            GameSaveRepository gameSaveRepository,
            ValidatingGameLoader validatingGameLoader)
        {
            _gameSaveRepository = gameSaveRepository;
            _validatingGameLoader = validatingGameLoader;
        }

        public void Load()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                string settlement = GetArg(args, "--tb-settlement");
                if (settlement == null)
                    return;

                string saveName = GetArg(args, "--tb-save");
                string saveDir = Path.Combine(UserDataFolder.Folder, "Saves");
                var settlementRef = new SettlementReference(settlement, saveDir);

                SaveReference saveRef;
                if (saveName != null)
                {
                    saveRef = new SaveReference(saveName, settlementRef);
                }
                else
                {
                    // pick most recent save in this settlement
                    saveRef = _gameSaveRepository.GetSaves(settlementRef).FirstOrDefault();
                    if (saveRef == null)
                    {
                        Debug.LogError($"[Timberbot] no saves found for settlement '{settlement}'");
                        return;
                    }
                }

                if (!_gameSaveRepository.SaveExists(saveRef))
                {
                    Debug.LogError($"[Timberbot] save not found: {saveRef}");
                    return;
                }

                Debug.Log($"[Timberbot] auto-loading: {saveRef}");
                _validatingGameLoader.LoadGame(saveRef);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Timberbot] auto-load failed: {ex}");
            }
        }

        private static string GetArg(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }
}
