using ImGuiNET;
using System.Numerics;

namespace XIVLauncher.Core.Components.Common;

public class Combo : Component
{
    public bool IsEnabled { get; set; } = true;
    public string Label { get; set; }

    private int _currentItem = 0;
    public string[] Items { get; set; }
    public Vector4 Color { get; set; }
    public Vector4 HoverColor { get; set; }
    public Vector4 TextColor { get; set; }

    public string Value
    {
        get => Items[_currentItem];
        set
        {
            int idx = Array.IndexOf(Items, value);
            _currentItem = idx == -1 ? 0 : idx;
        }
    }

    public event Action? Click;

    public int? Width { get; set; }

    public Combo(string label, string[] items, bool isEnabled = true, Vector4? color = null, Vector4? hoverColor = null, Vector4? textColor = null)
    {
        Label = label;
        Items = items;
        IsEnabled = isEnabled;
        Color = color ?? ImGuiColors.Blue;
        HoverColor = hoverColor ?? ImGuiColors.BlueShade3;
        TextColor = textColor ?? ImGuiColors.DalamudWhite;
        _currentItem = Program.Config.SelectedServer ?? 0;
    }

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ImGuiColors.BlueShade1);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ImGuiColors.BlueShade2);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ImGuiColors.BlueShade2);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, ImGuiColors.TextDisabled);
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.Text);

        ImGui.Text(Label);
        
        if (!IsEnabled)
        {
            ImGui.BeginDisabled();
        }
        
        if (ImGui.Combo($"###{Label}", ref _currentItem, Items, Items.Length))
        {
            this.Click?.Invoke();
            Program.Config.SelectedServer = _currentItem;
        }

        if (!IsEnabled)
        {
            ImGui.EndDisabled();
        }

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);
    }
}