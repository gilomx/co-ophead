using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Coophead
{
    internal static class MainMenuIntegration
    {
        private const int CoopheadMenuItem = 1000;

        private static readonly FieldInfo MainMenuItemsField =
            AccessTools.Field(typeof(SlotSelectScreen), "mainMenuItems");
        private static readonly FieldInfo AvailableItemsField =
            AccessTools.Field(typeof(SlotSelectScreen), "_availableMainMenuItems");
        private static readonly FieldInfo MainMenuSelectionField =
            AccessTools.Field(typeof(SlotSelectScreen), "_mainMenuSelection");
        private static readonly FieldInfo MainMenuChildField =
            AccessTools.Field(typeof(SlotSelectScreen), "mainMenuChild");

        internal static bool MenuOpen { get; private set; }

        public static void Install(SlotSelectScreen screen)
        {
            if (screen == null || screen.GetComponent<CoopheadMainMenuController>() != null)
                return;

            GameObject submenuRoot = null;
            GameObject entryObject = null;
            CoopheadMainMenuController controller = null;
            try
            {
                var menuItems = (Text[])MainMenuItemsField.GetValue(screen);
                var availableItems = (Array)AvailableItemsField.GetValue(screen);
                var mainMenuChild = (RectTransform)MainMenuChildField.GetValue(screen);
                if (menuItems == null || menuItems.Length == 0 || availableItems == null ||
                    availableItems.Length != menuItems.Length || mainMenuChild == null)
                    return;

                // Clone before the CO-OPHEAD row changes the original layout.
                submenuRoot = UnityEngine.Object.Instantiate(mainMenuChild.gameObject);
                submenuRoot.name = "CoopheadMenuRoot";
                submenuRoot.transform.SetParent(mainMenuChild.parent, false);
                CopyRectTransform(mainMenuChild, (RectTransform)submenuRoot.transform);
                submenuRoot.transform.SetSiblingIndex(mainMenuChild.GetSiblingIndex() + 1);
                submenuRoot.SetActive(false);

                var source = menuItems[0];
                entryObject = UnityEngine.Object.Instantiate(source.gameObject);
                entryObject.name = "CoopheadMainMenuItem";
                entryObject.transform.SetParent(source.transform.parent, false);
                entryObject.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
                var entry = entryObject.GetComponent<Text>();
                if (entry == null)
                    throw new InvalidOperationException("La entrada del menú no contiene texto.");
                DisableDynamicLayout(entryObject, false);
                SetAnimatedText(entry, "CO-OPHEAD");

                var itemType = availableItems.GetType().GetElementType();
                if (itemType == null)
                    throw new InvalidOperationException("No se encontró el tipo del menú principal.");

                var expandedMenu = new Text[menuItems.Length + 1];
                expandedMenu[0] = menuItems[0];
                expandedMenu[1] = entry;
                for (var i = 1; i < menuItems.Length; i++)
                    expandedMenu[i + 1] = menuItems[i];

                var expandedItems = Array.CreateInstance(itemType, availableItems.Length + 1);
                expandedItems.SetValue(availableItems.GetValue(0), 0);
                expandedItems.SetValue(Enum.ToObject(itemType, CoopheadMenuItem), 1);
                for (var i = 1; i < availableItems.Length; i++)
                    expandedItems.SetValue(availableItems.GetValue(i), i + 1);

                var selectedColor = (Color)AccessTools.Field(typeof(SlotSelectScreen),
                    "mainMenuSelectedColor").GetValue(screen);
                var unselectedColor = (Color)AccessTools.Field(typeof(SlotSelectScreen),
                    "mainMenuUnselectedColor").GetValue(screen);

                controller = screen.gameObject.AddComponent<CoopheadMainMenuController>();
                if (!controller.Initialize(screen, entry, mainMenuChild, submenuRoot,
                    selectedColor, unselectedColor))
                    throw new InvalidOperationException("No se pudieron preparar las filas del submenú.");

                var firstPosition = source.rectTransform.anchoredPosition;
                var step = menuItems.Length > 1
                    ? menuItems[1].rectTransform.anchoredPosition - firstPosition
                    : new Vector2(0f, -38f);
                if (step.sqrMagnitude < 1f)
                    step = new Vector2(0f, -38f);

                for (var i = 0; i < menuItems.Length; i++)
                {
                    var offset = i == 0 ? step * -0.5f : step * 0.5f;
                    menuItems[i].rectTransform.anchoredPosition += offset;
                }
                entry.rectTransform.anchoredPosition = firstPosition + step * 0.5f;

                // Commit only after every cloned element has been validated.
                MainMenuItemsField.SetValue(screen, expandedMenu);
                AvailableItemsField.SetValue(screen, expandedItems);
                submenuRoot = null;
                entryObject = null;
                controller = null;
                Plugin.Log.LogInfo("[Menu] CO-OPHEAD agregado como submenú nativo.");
            }
            catch (Exception ex)
            {
                if (controller != null)
                    UnityEngine.Object.Destroy(controller);
                if (submenuRoot != null)
                    UnityEngine.Object.Destroy(submenuRoot);
                if (entryObject != null)
                    UnityEngine.Object.Destroy(entryObject);
                Plugin.Log.LogWarning("[Menu] No se pudo integrar el menú principal: " + ex.Message);
            }
        }

        public static bool BeforeMainMenuUpdate(SlotSelectScreen screen)
        {
            if (RemoteInputLab.LocalPhysicalInputBlocked)
                return false;
            var controller = screen == null ? null :
                screen.GetComponent<CoopheadMainMenuController>();
            if (controller == null)
                return true;
            if (controller.PanelOpen || controller.BlockMainMenuUpdate)
                return false;
            if (!IsCoopheadSelected(screen) || !controller.AcceptPressed())
                return true;

            AudioManager.Play("level_menu_select");
            controller.Open();
            return false;
        }

        internal static void SetMenuOpen(bool open)
        {
            MenuOpen = open;
        }

        internal static void SetAnimatedText(Text text, string value)
        {
            if (text == null)
                return;
            var animator = text.GetComponent<UITextAnimator>();
            if (animator != null)
                animator.SetString(value);
            text.text = value;
        }

        internal static void DisableDynamicLayout(GameObject target, bool disableLanguageLayout)
        {
            if (target == null)
                return;
            var behaviours = target.GetComponentsInChildren<MonoBehaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;
                var typeName = behaviour.GetType().Name;
                if (typeName == "LocalizationHelper" ||
                    (disableLanguageLayout && typeName == "CustomLanguageLayoutGroup"))
                    behaviour.enabled = false;
            }
        }

        private static bool IsCoopheadSelected(SlotSelectScreen screen)
        {
            try
            {
                var selection = (int)MainMenuSelectionField.GetValue(screen);
                var items = (Array)AvailableItemsField.GetValue(screen);
                return selection >= 0 && selection < items.Length &&
                    Convert.ToInt32(items.GetValue(selection)) == CoopheadMenuItem;
            }
            catch
            {
                return false;
            }
        }

        private static void CopyRectTransform(RectTransform source, RectTransform target)
        {
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.anchoredPosition = source.anchoredPosition;
            target.sizeDelta = source.sizeDelta;
            target.pivot = source.pivot;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }
    }

    internal sealed class CoopheadMainMenuController : MonoBehaviour
    {
        private enum Page
        {
            Root,
            HostWaiting,
            HostReady,
            JoinEntry,
            GuestJoining,
            GuestWaiting
        }

        private const int RoomCodeLength = 6;
        private const string RoomAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private static readonly FieldInfo AnyPlayerPlayersField =
            AccessTools.Field(typeof(CupheadInput.AnyPlayerInput), "players");
        private static readonly FieldInfo SceneLoaderIconField =
            AccessTools.Field(typeof(SceneLoader), "icon");
        private static readonly MethodInfo SetStateMethod =
            AccessTools.Method(typeof(SlotSelectScreen), "SetState",
                new Type[] { typeof(SlotSelectScreen.State) });
        private static readonly FieldInfo SlotsField =
            AccessTools.Field(typeof(SlotSelectScreen), "slots");

        private SlotSelectScreen screen;
        private Text menuEntry;
        private UITextAnimator menuEntryAnimator;
        private RectTransform mainMenuChild;
        private GameObject panelRoot;
        private CanvasGroup panelCanvasGroup;
        private Text[] rows;
        private UITextAnimator[] rowAnimators;
        private string[] labels;
        private string[] appliedLabels;
        private bool[] actionable;
        private Color selectedColor;
        private Color unselectedColor;
        private CupheadInput.AnyPlayerInput menuInput;
        private Page page;
        private int visibleRows;
        private int selection;
        private int openedFrame;
        private int blockMainMenuUntilFrame;
        private int caretIndex;
        private string joinCode = string.Empty;
        private bool joinWasComplete;
        private bool codeInputConsumedThisFrame;
        private string lastHostCode = string.Empty;
        private float copyFeedbackUntil;
        private bool saveSelectionPending;
        private int saveSelectionRequestedFrame;
        private GameObject loaderObject;
        private RectTransform loaderRect;
        private Image loaderImage;
        private Text loaderRow;
        private bool loaderUnavailable;
        private int lastVisibleRows = -1;

        public bool PanelOpen { get; private set; }
        public bool BlockMainMenuUpdate => Time.frameCount <= blockMainMenuUntilFrame;

        public bool Initialize(SlotSelectScreen owner, Text entry, RectTransform menuRoot,
            GameObject submenuRoot, Color selected, Color unselected)
        {
            screen = owner;
            menuEntry = entry;
            menuEntryAnimator = entry == null ? null : entry.GetComponent<UITextAnimator>();
            mainMenuChild = menuRoot;
            panelRoot = submenuRoot;
            selectedColor = selected;
            unselectedColor = unselected;
            menuInput = new CupheadInput.AnyPlayerInput(false);
            RestrictInputToLocalPlayerOne();
            return PrepareSubmenu();
        }

        public bool AcceptPressed()
        {
            return !RemoteInputLab.LocalPhysicalInputBlocked && IsAcceptDown();
        }

        public void Open()
        {
            if (panelRoot == null || rows == null || rows.Length < 4)
                return;

            var plugin = Plugin.Instance;
            if (plugin != null)
                plugin.HideFallbackOnlineWindow();

            if (RemoteInputLab.IsHostSession)
                page = RemoteInputLab.LoadoutHandshakeReady ?
                    Page.HostReady : Page.HostWaiting;
            else if (RemoteInputLab.IsClientSession)
                page = RemoteInputLab.LoadoutHandshakeReady ?
                    Page.GuestWaiting : Page.GuestJoining;
            else
                page = Page.Root;

            PanelOpen = true;
            MainMenuIntegration.SetMenuOpen(true);
            selection = -1;
            openedFrame = Time.frameCount;
            mainMenuChild.gameObject.SetActive(false);
            panelRoot.SetActive(true);
            panelCanvasGroup.alpha = 1f;
            RenderPage();
        }

        private void Update()
        {
            KeepEntryLabel();
            if (!PanelOpen)
                return;

            if (RemoteInputLab.LocalPhysicalInputBlocked)
            {
                UpdateSessionPage();
                RenderPage();
                UpdateLoaderPosition();
                codeInputConsumedThisFrame = false;
                return;
            }

            if (saveSelectionPending)
            {
                if (Time.frameCount > saveSelectionRequestedFrame &&
                    !IsAcceptHeld())
                    CompleteNativeSaveSelection();
                return;
            }

            UpdateSessionPage();
            if (page == Page.JoinEntry)
                ReadRoomCodeInput();
            RenderPage();
            UpdateLoaderPosition();

            if (Time.frameCount == openedFrame)
                return;

            var moved = HandleJoinCodeNavigation();
            if (!moved && IsMenuDown())
            {
                moved = MoveSelection(1);
            }
            else if (!moved && IsMenuUp())
            {
                moved = MoveSelection(-1);
            }
            if (moved)
            {
                AudioManager.Play("level_menu_move");
                ApplyRowColors();
            }

            if (Input.GetKeyDown(KeyCode.Escape) ||
                (!codeInputConsumedThisFrame && IsCancelDown()))
            {
                AudioManager.Play("level_menu_select");
                HandleCancel();
                return;
            }

            if (!moved && !codeInputConsumedThisFrame && IsAcceptDown() &&
                IsSelectionActionable())
            {
                AudioManager.Play("level_menu_select");
                ActivateSelection();
                RenderPage();
            }
        }

        private void OnDestroy()
        {
            DestroyLoader();
            if (PanelOpen)
                MainMenuIntegration.SetMenuOpen(false);
            // The online session intentionally survives when the host enters a save.
        }

        private void LateUpdate()
        {
            if (PanelOpen)
                UpdateLoaderPosition();
        }

        private bool PrepareSubmenu()
        {
            if (panelRoot == null)
                return false;

            MainMenuIntegration.DisableDynamicLayout(panelRoot, true);
            var parent = FindRowsParent(panelRoot.transform);
            var foundRows = new List<Text>();
            if (parent != null)
            {
                for (var i = 0; i < parent.childCount; i++)
                {
                    var row = parent.GetChild(i).GetComponent<Text>();
                    if (row != null)
                        foundRows.Add(row);
                }
            }
            if (foundRows.Count < 4)
            {
                foundRows.Clear();
                foundRows.AddRange(panelRoot.GetComponentsInChildren<Text>(true));
            }
            if (foundRows.Count < 4)
                return false;

            rows = foundRows.ToArray();
            rowAnimators = new UITextAnimator[rows.Length];
            labels = new string[rows.Length];
            appliedLabels = new string[rows.Length];
            actionable = new bool[rows.Length];
            for (var i = 0; i < rows.Length; i++)
            {
                rowAnimators[i] = rows[i].GetComponent<UITextAnimator>();
                rows[i].gameObject.SetActive(i < 4);
            }

            panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
                panelCanvasGroup = panelRoot.AddComponent<CanvasGroup>();
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
            return true;
        }

        private static Transform FindRowsParent(Transform root)
        {
            Transform best = null;
            var bestCount = 0;
            FindRowsParent(root, ref best, ref bestCount);
            return bestCount >= 4 ? best : null;
        }

        private static void FindRowsParent(Transform current, ref Transform best, ref int bestCount)
        {
            var directTextChildren = 0;
            for (var i = 0; i < current.childCount; i++)
                if (current.GetChild(i).GetComponent<Text>() != null)
                    directTextChildren++;
            if (directTextChildren > bestCount)
            {
                best = current;
                bestCount = directTextChildren;
            }
            for (var i = 0; i < current.childCount; i++)
                FindRowsParent(current.GetChild(i), ref best, ref bestCount);
        }

        private void RestrictInputToLocalPlayerOne()
        {
            try
            {
                if (AnyPlayerPlayersField == null)
                    return;
                var playerOne = PlayerManager.GetPlayerInput(PlayerId.PlayerOne);
                if (playerOne != null)
                    AnyPlayerPlayersField.SetValue(menuInput,
                        new Rewired.Player[] { playerOne });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Menu] No se pudo limitar el lobby al jugador local: " +
                    ex.Message);
            }
        }

        private void UpdateSessionPage()
        {
            Page desired;
            if (RemoteInputLab.IsHostSession)
                desired = RemoteInputLab.LoadoutHandshakeReady ?
                    Page.HostReady : Page.HostWaiting;
            else if (RemoteInputLab.IsClientSession)
                desired = RemoteInputLab.LoadoutHandshakeReady ?
                    Page.GuestWaiting : Page.GuestJoining;
            else
            {
                var failedAttempt = (page == Page.HostWaiting || page == Page.GuestJoining) &&
                    HasSessionError();
                if (!failedAttempt && page != Page.Root && page != Page.JoinEntry)
                    ChangePage(Page.Root);
                return;
            }

            if (page != desired)
                ChangePage(desired);
        }

        private void ActivateSelection()
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
                return;

            switch (page)
            {
                case Page.Root:
                    if (selection == 0)
                    {
                        ChangePage(Page.HostWaiting);
                        plugin.CreateRoom();
                    }
                    else if (selection == 1)
                    {
                        caretIndex = joinCode.Length;
                        ChangePage(Page.JoinEntry);
                    }
                    else if (selection == 2)
                    {
                        CloseToMainMenu();
                    }
                    break;

                case Page.HostWaiting:
                    if (selection == 1 && !string.IsNullOrEmpty(RemoteInputLab.CurrentRoomCode))
                    {
                        if (plugin.CopyRoomCode())
                            copyFeedbackUntil = Time.unscaledTime + 1.25f;
                    }
                    else if (selection == visibleRows - 1)
                    {
                        StopAndReturnToRoot(plugin);
                    }
                    break;

                case Page.HostReady:
                    if (selection == 1)
                        RequestNativeSaveSelection();
                    else if (selection == 2)
                        CloseToMainMenu();
                    else if (selection == 3)
                        StopAndReturnToRoot(plugin);
                    break;

                case Page.JoinEntry:
                    if (selection == 0 && joinCode.Length == RoomCodeLength)
                    {
                        selection = 1;
                        ApplyRowColors();
                    }
                    else if (selection == 1 && joinCode.Length == RoomCodeLength)
                    {
                        ChangePage(Page.GuestJoining);
                        plugin.JoinRoom(joinCode);
                    }
                    else if (selection == 2)
                    {
                        ChangePage(Page.Root);
                    }
                    break;

                case Page.GuestJoining:
                case Page.GuestWaiting:
                    if (selection == visibleRows - 1)
                        StopAndReturnToRoot(plugin);
                    break;
            }
        }

        private void HandleCancel()
        {
            var plugin = Plugin.Instance;
            switch (page)
            {
                case Page.Root:
                    CloseToMainMenu();
                    break;
                case Page.HostReady:
                    CloseToMainMenu();
                    break;
                case Page.JoinEntry:
                    ChangePage(Page.Root);
                    break;
                default:
                    if (plugin != null)
                        StopAndReturnToRoot(plugin);
                    else
                        ChangePage(Page.Root);
                    break;
            }
        }

        private void StopAndReturnToRoot(Plugin plugin)
        {
            if (RemoteInputLab.Enabled)
                plugin.StopOnline();
            ChangePage(Page.Root);
        }

        private void ChangePage(Page next)
        {
            page = next;
            selection = -1;
            if (next == Page.JoinEntry)
                joinWasComplete = false;
            DestroyLoader();
            RenderPage();
        }

        private void CloseToMainMenu()
        {
            PanelOpen = false;
            saveSelectionPending = false;
            MainMenuIntegration.SetMenuOpen(false);
            DestroyLoader();
            panelCanvasGroup.alpha = 0f;
            mainMenuChild.gameObject.SetActive(true);
            blockMainMenuUntilFrame = Time.frameCount;
        }

        private void RequestNativeSaveSelection()
        {
            saveSelectionPending = true;
            saveSelectionRequestedFrame = Time.frameCount;
        }

        private void CompleteNativeSaveSelection()
        {
            try
            {
                if (SetStateMethod == null || SlotsField == null)
                    throw new MissingMemberException("No se encontró el selector de partidas.");

                var slots = (SlotSelectScreenSlot[])SlotsField.GetValue(screen);
                var count = Math.Min(3, slots == null ? 0 : slots.Length);
                if (count == 0)
                    throw new InvalidOperationException("No hay partidas disponibles para inicializar.");
                for (var i = 0; i < count; i++)
                    if (slots[i] == null)
                        throw new InvalidOperationException("Una partida no está disponible.");

                SetStateMethod.Invoke(screen,
                    new object[] { SlotSelectScreen.State.SlotSelect });
                for (var i = 0; i < count; i++)
                    slots[i].Init(i);

                PanelOpen = false;
                saveSelectionPending = false;
                MainMenuIntegration.SetMenuOpen(false);
                DestroyLoader();
                panelCanvasGroup.alpha = 0f;
                blockMainMenuUntilFrame = Time.frameCount;
            }
            catch (Exception ex)
            {
                saveSelectionPending = false;
                try
                {
                    if (SetStateMethod != null)
                        SetStateMethod.Invoke(screen,
                            new object[] { SlotSelectScreen.State.MainMenu });
                    mainMenuChild.gameObject.SetActive(false);
                    panelCanvasGroup.alpha = 1f;
                }
                catch
                {
                    // The original exception is the useful one for diagnosing this transition.
                }
                var inner = ex.InnerException == null ? ex : ex.InnerException;
                Plugin.Log.LogWarning("[Menu] No se pudo abrir el selector de partidas: " +
                    inner.Message);
            }
        }

        private void ReadRoomCodeInput()
        {
            codeInputConsumedThisFrame = false;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                if (Input.GetKeyDown(KeyCode.V))
                {
                    PasteRoomCode();
                    codeInputConsumedThisFrame = true;
                }
                return;
            }
            if (Input.GetKeyDown(KeyCode.Home))
            {
                caretIndex = 0;
                return;
            }
            if (Input.GetKeyDown(KeyCode.End))
            {
                caretIndex = joinCode.Length;
                return;
            }
            if (Input.GetKeyDown(KeyCode.Backspace) && caretIndex > 0)
            {
                joinCode = joinCode.Remove(caretIndex - 1, 1);
                caretIndex--;
                codeInputConsumedThisFrame = true;
                return;
            }
            if (Input.GetKeyDown(KeyCode.Delete) && caretIndex < joinCode.Length)
            {
                joinCode = joinCode.Remove(caretIndex, 1);
                codeInputConsumedThisFrame = true;
                return;
            }

            var typed = Input.inputString;
            for (var i = 0; i < typed.Length && joinCode.Length < RoomCodeLength; i++)
            {
                var character = char.ToUpperInvariant(typed[i]);
                if (!IsRoomCharacter(character))
                    continue;
                joinCode = joinCode.Insert(caretIndex, character.ToString());
                caretIndex++;
                codeInputConsumedThisFrame = true;
            }
        }

        private void PasteRoomCode()
        {
            joinCode = FilterRoomCode(GUIUtility.systemCopyBuffer);
            caretIndex = joinCode.Length;
        }

        private static string FilterRoomCode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            var result = string.Empty;
            value = value.ToUpperInvariant();
            for (var i = 0; i < value.Length && result.Length < RoomCodeLength; i++)
                if (IsRoomCharacter(value[i]))
                    result += value[i];
            return result;
        }

        private static bool IsRoomCharacter(char character)
        {
            return RoomAlphabet.IndexOf(character) >= 0;
        }

        private bool HandleJoinCodeNavigation()
        {
            if (page != Page.JoinEntry || selection != 0)
                return false;
            if (IsMenuLeft())
            {
                caretIndex = Math.Max(0, caretIndex - 1);
                return true;
            }
            if (IsMenuRight())
            {
                caretIndex = Math.Min(joinCode.Length, caretIndex + 1);
                return true;
            }
            if (IsMenuUp())
            {
                CycleJoinCharacter(-1);
                return true;
            }
            if (IsMenuDown())
            {
                CycleJoinCharacter(1);
                return true;
            }
            return false;
        }

        private void CycleJoinCharacter(int direction)
        {
            caretIndex = Mathf.Clamp(caretIndex, 0, joinCode.Length);
            if (caretIndex == joinCode.Length)
            {
                if (joinCode.Length >= RoomCodeLength)
                    return;
                var firstIndex = direction < 0 ? RoomAlphabet.Length - 1 : 0;
                joinCode += RoomAlphabet[firstIndex];
                return;
            }

            var current = RoomAlphabet.IndexOf(joinCode[caretIndex]);
            if (current < 0)
                current = 0;
            current = (current + direction + RoomAlphabet.Length) % RoomAlphabet.Length;
            joinCode = joinCode.Remove(caretIndex, 1).Insert(caretIndex,
                RoomAlphabet[current].ToString());
        }

        private void RenderPage()
        {
            if (rows == null)
                return;
            ClearRows();
            var loaderIndex = -1;
            var hasError = HasSessionError();
            var status = CompactStatus();

            switch (page)
            {
                case Page.Root:
                    AddRow("CREAR PARTIDA", true);
                    AddRow("UNIRSE", true);
                    AddRow("VOLVER", true);
                    break;

                case Page.HostWaiting:
                    var hostCode = RemoteInputLab.CurrentRoomCode;
                    if (hasError)
                    {
                        AddRow("NO SE PUDO CREAR", false);
                        AddRow(status, false);
                        AddRow("VOLVER", true);
                    }
                    else if (string.IsNullOrEmpty(hostCode))
                    {
                        AddRow("CREANDO SALA", false);
                        loaderIndex = 0;
                        AddRow(status, false);
                        AddRow("VOLVER", true);
                    }
                    else
                    {
                        AddRow("CÓDIGO: " + hostCode, false);
                        AddRow(Time.unscaledTime < copyFeedbackUntil ?
                            "CÓDIGO COPIADO" : "COPIAR CÓDIGO", true);
                        AddRow("ESPERANDO INVITADO", false);
                        loaderIndex = 2;
                        AddRow("VOLVER", true);
                        if (string.IsNullOrEmpty(lastHostCode))
                            selection = 1;
                    }
                    lastHostCode = hostCode ?? string.Empty;
                    break;

                case Page.HostReady:
                    AddRow("INVITADO CONECTADO", false);
                    AddRow("EMPEZAR", true);
                    AddRow("VOLVER", true);
                    AddRow("DESCONECTAR", true);
                    break;

                case Page.JoinEntry:
                    var codeComplete = joinCode.Length == RoomCodeLength;
                    AddRow(EditableCodeLabel(), true);
                    AddRow("UNIRSE", codeComplete);
                    AddRow("VOLVER", true);
                    if (codeComplete && !joinWasComplete)
                        selection = 1;
                    joinWasComplete = codeComplete;
                    break;

                case Page.GuestJoining:
                    if (hasError)
                    {
                        AddRow("NO SE PUDO UNIR", false);
                        AddRow(status, false);
                        AddRow("VOLVER", true);
                    }
                    else
                    {
                        AddRow("UNIÉNDOSE", false);
                        loaderIndex = 0;
                        AddRow(status, false);
                        AddRow("CANCELAR", true);
                    }
                    break;

                case Page.GuestWaiting:
                    AddRow("CONECTADO", false);
                    AddRow("ESPERANDO AL ANFITRIÓN", false);
                    loaderIndex = 1;
                    AddRow("DESCONECTAR", true);
                    break;
            }

            EnsureActionableSelection();
            ApplyRows();
            if (loaderIndex >= 0 && !hasError)
            {
                if (!EnsureNativeLoader(rows[loaderIndex]))
                    SetRowLabel(loaderIndex, labels[loaderIndex] + AnimatedDots());
            }
            else
            {
                DestroyLoader();
            }

            if (lastVisibleRows != visibleRows)
            {
                lastVisibleRows = visibleRows;
                var rect = panelRoot.transform as RectTransform;
                if (rect != null)
                    LayoutRebuilder.MarkLayoutForRebuild(rect);
            }
        }

        private void ClearRows()
        {
            visibleRows = 0;
            for (var i = 0; i < rows.Length; i++)
            {
                labels[i] = string.Empty;
                actionable[i] = false;
            }
        }

        private void AddRow(string label, bool isActionable)
        {
            if (visibleRows >= rows.Length)
                return;
            labels[visibleRows] = label ?? string.Empty;
            actionable[visibleRows] = isActionable;
            visibleRows++;
        }

        private void ApplyRows()
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var visible = i < visibleRows;
                if (rows[i].gameObject.activeSelf != visible)
                    rows[i].gameObject.SetActive(visible);
                if (!visible)
                    continue;
                SetRowLabel(i, labels[i]);
            }
            ApplyRowColors();
        }

        private void SetRowLabel(int index, string label)
        {
            if (index < 0 || index >= rows.Length)
                return;
            labels[index] = label;
            if (appliedLabels[index] == label)
                return;
            appliedLabels[index] = label;
            if (rowAnimators[index] != null)
                rowAnimators[index].SetString(label);
            rows[index].text = label;
        }

        private void ApplyRowColors()
        {
            for (var i = 0; i < visibleRows; i++)
                rows[i].color = actionable[i] && i == selection ?
                    selectedColor : unselectedColor;
        }

        private bool MoveSelection(int direction)
        {
            if (visibleRows == 0)
                return false;
            var next = selection;
            for (var i = 0; i < visibleRows; i++)
            {
                next += direction;
                if (next < 0)
                    next = visibleRows - 1;
                else if (next >= visibleRows)
                    next = 0;
                if (actionable[next])
                {
                    selection = next;
                    return true;
                }
            }
            return false;
        }

        private void EnsureActionableSelection()
        {
            if (selection >= 0 && selection < visibleRows && actionable[selection])
                return;
            selection = -1;
            for (var i = 0; i < visibleRows; i++)
            {
                if (!actionable[i])
                    continue;
                selection = i;
                return;
            }
        }

        private bool IsSelectionActionable()
        {
            return selection >= 0 && selection < visibleRows && actionable[selection];
        }

        private string EditableCodeLabel()
        {
            if (caretIndex < 0)
                caretIndex = 0;
            if (caretIndex > joinCode.Length)
                caretIndex = joinCode.Length;
            var padded = joinCode + new string('_', RoomCodeLength - joinCode.Length);
            var caret = Mathf.FloorToInt(Time.unscaledTime * 2f) % 2 == 0 ? "|" : " ";
            return "CÓDIGO: " + padded.Insert(caretIndex, caret);
        }

        private bool HasSessionError()
        {
            var plugin = Plugin.Instance;
            var message = plugin == null ? string.Empty : plugin.OnlineMessage;
            var status = plugin == null ? RemoteInputLab.TransportStatus : plugin.TransportStatus;
            return ContainsError(message) || ContainsError(status);
        }

        private static bool ContainsError(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                value.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string CompactStatus()
        {
            var plugin = Plugin.Instance;
            var message = plugin == null ? string.Empty : plugin.OnlineMessage;
            var status = plugin == null ? RemoteInputLab.TransportStatus : plugin.TransportStatus;
            var value = ContainsError(message) ? message : status;
            if (string.IsNullOrEmpty(value))
                value = "PREPARANDO CONEXIÓN";
            value = value.Replace('\r', ' ').Replace('\n', ' ').ToUpperInvariant();
            const int maxLength = 34;
            return value.Length <= maxLength ? value :
                value.Substring(0, maxLength - 3) + "...";
        }

        private static string AnimatedDots()
        {
            var count = Mathf.FloorToInt(Time.unscaledTime * 3f) % 3 + 1;
            return new string('.', count);
        }

        private bool EnsureNativeLoader(Text targetRow)
        {
            if (targetRow == null || loaderUnavailable)
                return false;
            if (loaderObject != null && loaderRow == targetRow)
                return true;
            DestroyLoader();

            try
            {
                if (!SceneLoader.Exists || SceneLoaderIconField == null)
                {
                    loaderUnavailable = true;
                    return false;
                }
                var source = (Image)SceneLoaderIconField.GetValue(SceneLoader.instance);
                if (source == null)
                {
                    loaderUnavailable = true;
                    return false;
                }

                loaderObject = UnityEngine.Object.Instantiate(source.gameObject);
                loaderObject.name = "CoopheadHourglass";
                loaderObject.transform.SetParent(targetRow.transform, false);
                SetLayerRecursively(loaderObject, targetRow.gameObject.layer);
                loaderRect = loaderObject.GetComponent<RectTransform>();
                if (loaderRect != null)
                {
                    loaderRect.anchorMin = new Vector2(0.5f, 0.5f);
                    loaderRect.anchorMax = new Vector2(0.5f, 0.5f);
                    loaderRect.pivot = new Vector2(0.5f, 0.5f);
                    loaderRect.localRotation = Quaternion.identity;
                    loaderRect.localScale = Vector3.one;
                    loaderRect.sizeDelta = new Vector2(32f, 32f);
                }

                loaderImage = loaderObject.GetComponent<Image>();
                if (loaderImage != null)
                {
                    var color = loaderImage.color;
                    color.a = 1f;
                    loaderImage.color = color;
                    loaderImage.preserveAspect = true;
                    loaderImage.raycastTarget = false;
                }
                loaderObject.SetActive(true);
                var animator = loaderObject.GetComponent("Animator");
                if (animator != null)
                {
                    var setTrigger = AccessTools.Method(animator.GetType(), "SetTrigger",
                        new Type[] { typeof(string) });
                    if (setTrigger != null)
                        setTrigger.Invoke(animator, new object[] { "Hourglass" });
                }
                loaderRow = targetRow;
                UpdateLoaderPosition();
                return true;
            }
            catch (Exception ex)
            {
                DestroyLoader();
                loaderUnavailable = true;
                Plugin.Log.LogWarning("[Menu] No se pudo clonar el reloj de arena: " +
                    ex.Message);
                return false;
            }
        }

        private void UpdateLoaderPosition()
        {
            if (loaderRect == null || loaderRow == null)
                return;
            // The native Animator also keys the source RectTransform. Reapply the lobby
            // placement in LateUpdate so only its original sprite animation survives.
            loaderRect.anchorMin = new Vector2(0.5f, 0.5f);
            loaderRect.anchorMax = new Vector2(0.5f, 0.5f);
            loaderRect.pivot = new Vector2(0.5f, 0.5f);
            loaderRect.localRotation = Quaternion.identity;
            loaderRect.localScale = Vector3.one;
            loaderRect.sizeDelta = new Vector2(38f, 38f);
            var offset = Mathf.Clamp(loaderRow.preferredWidth * 0.5f + 30f, 95f, 225f);
            loaderRect.anchoredPosition = new Vector2(-offset, 0f);
            if (loaderImage != null)
            {
                var color = loaderImage.color;
                color.a = 1f;
                loaderImage.color = color;
            }
        }

        private void DestroyLoader()
        {
            if (loaderObject != null)
                UnityEngine.Object.Destroy(loaderObject);
            loaderObject = null;
            loaderRect = null;
            loaderImage = null;
            loaderRow = null;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            for (var i = 0; i < target.transform.childCount; i++)
                SetLayerRecursively(target.transform.GetChild(i).gameObject, layer);
        }

        private void KeepEntryLabel()
        {
            if (menuEntry == null)
                return;
            if (menuEntryAnimator != null)
                menuEntryAnimator.SetString("CO-OPHEAD");
            if (menuEntry.text != "CO-OPHEAD" && menuEntryAnimator == null)
                menuEntry.text = "CO-OPHEAD";
        }

        private bool IsAcceptDown()
        {
            return (menuInput != null && menuInput.GetButtonDown((CupheadButton)13)) ||
                Input.GetKeyDown(KeyCode.Z);
        }

        private bool IsAcceptHeld()
        {
            return (menuInput != null && menuInput.GetButton((CupheadButton)13)) ||
                Input.GetKey(KeyCode.Z);
        }

        private bool IsCancelDown()
        {
            return (menuInput != null && menuInput.GetButtonDown((CupheadButton)14)) ||
                Input.GetKeyDown(KeyCode.X);
        }

        private bool IsMenuUp()
        {
            return (menuInput != null && menuInput.GetButtonDown((CupheadButton)16)) ||
                Input.GetKeyDown(KeyCode.UpArrow);
        }

        private bool IsMenuDown()
        {
            return (menuInput != null && menuInput.GetButtonDown((CupheadButton)19)) ||
                Input.GetKeyDown(KeyCode.DownArrow);
        }

        private bool IsMenuLeft()
        {
            return (menuInput != null && menuInput.GetButtonDown((CupheadButton)18)) ||
                Input.GetKeyDown(KeyCode.LeftArrow);
        }

        private bool IsMenuRight()
        {
            return (menuInput != null && menuInput.GetButtonDown((CupheadButton)20)) ||
                Input.GetKeyDown(KeyCode.RightArrow);
        }
    }

    [HarmonyPatch(typeof(SlotSelectScreen), "Awake")]
    internal static class SlotSelectScreenAwakePatch
    {
        private static void Postfix(SlotSelectScreen __instance)
        {
            MainMenuIntegration.Install(__instance);
        }
    }

    [HarmonyPatch(typeof(SlotSelectScreen), "UpdateMainMenu")]
    internal static class SlotSelectScreenMainMenuPatch
    {
        private static bool Prefix(SlotSelectScreen __instance)
        {
            return MainMenuIntegration.BeforeMainMenuUpdate(__instance);
        }
    }

    [HarmonyPatch]
    internal static class SlotSelectFrontendLocalInputPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var inputType = typeof(CupheadInput.AnyPlayerInput);
            var names = new[]
            {
                "GetButton",
                "GetButtonDown",
                "GetButtonUp",
                "GetActionButtonDown",
                "GetAnyButtonDown",
                "GetAnyButtonHeld"
            };
            for (var i = 0; i < names.Length; i++)
            {
                var method = AccessTools.Method(inputType, names[i]);
                if (method != null)
                    yield return method;
            }
        }

        private static void Prefix(ref Rewired.Player[] ___players,
            out Rewired.Player[] __state)
        {
            __state = null;
            if (!ShouldReserveFrontendForPlayerOne())
                return;

            Rewired.Player playerOne;
            try
            {
                playerOne = PlayerManager.GetPlayerInput(PlayerId.PlayerOne);
            }
            catch
            {
                return;
            }
            if (playerOne == null || (___players != null && ___players.Length == 1 &&
                object.ReferenceEquals(___players[0], playerOne)))
                return;

            __state = ___players;
            ___players = new Rewired.Player[] { playerOne };
        }

        private static void Postfix(ref Rewired.Player[] ___players,
            Rewired.Player[] __state)
        {
            if (__state != null)
                ___players = __state;
        }

        private static Exception Finalizer(Exception __exception,
            ref Rewired.Player[] ___players, Rewired.Player[] __state)
        {
            if (__state != null)
                ___players = __state;
            return __exception;
        }

        private static bool ShouldReserveFrontendForPlayerOne()
        {
            if (!RemoteInputLab.Enabled)
                return false;
            try
            {
                return string.Equals(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    "scene_slot_select", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
