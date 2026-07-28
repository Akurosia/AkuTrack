using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;

namespace AkuTrack.Windows
{
    public class MapContextMenu
    {
        public void Draw(Action placeFlag)
        {
            using var contextMenu = ImRaii.ContextPopup("AkuTrack_Context_Menu");
            if (!contextMenu) return;

            if (ImGui.MenuItem("Place Flag"))
            {
                placeFlag();
            }
        }
    }
}
