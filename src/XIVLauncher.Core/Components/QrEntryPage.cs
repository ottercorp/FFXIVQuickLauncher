using System.Numerics;
using ImGuiNET;

namespace XIVLauncher.Core.Components;

public class QrEntryPage : Page
{
    private string otp = string.Empty;
    // private bool show = false;


    public bool Cancelled { get; private set; }

    // private OtpListener? qrListener;
    private readonly string qrPath = Path.Combine(Environment.CurrentDirectory, "Resources", "QR.png");
    private TextureWrap qrImage;

    public QrEntryPage(LauncherApp app)
        : base(app)
    {
    }

    public void CloseDialog()
    {
        App.State = LauncherApp.LauncherState.Main;
    }

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 7f);

        var childSize = new Vector2(300, 200);
        var vpSize = ImGuiHelpers.ViewportSize;

        ImGui.SetNextWindowPos(new Vector2(vpSize.X / 2 - childSize.X / 2, vpSize.Y / 2 - childSize.Y / 2), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.4f);

        if (ImGui.BeginChild("###qr", childSize, true, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar))
        {
            // center text in window
            ImGuiHelpers.CenteredText("请扫描二维码");

            if (File.Exists(qrPath))
            {
                ImGui.Dummy(new Vector2(10, 10));
                FileInfo fileInfo = new FileInfo(qrPath);
                var data = new byte[fileInfo.Length];

                using (FileStream fs = fileInfo.OpenRead())
                {
                    fs.Read(data, 0, data.Length);
                }

                qrImage = TextureWrap.Load(data);
                var bPos = ImGui.GetWindowPos();
                var posX = (ImGui.GetWindowSize().X - qrImage.Size.X) * 0.5f;
                var posY = ImGui.GetCursorPosY();
                var drawList = ImGui.GetWindowDrawList();
                drawList.AddRectFilled(new Vector2(bPos.X + posX - 10, bPos.Y + posY - 10),
                    new Vector2(bPos.X + posX + qrImage.Size.X + 10, bPos.Y + posY + qrImage.Size.Y + 10), 0xffffffff);
                ImGui.SetCursorPosX(posX);
                ImGui.Image(qrImage.ImGuiHandle, qrImage.Size);
                ImGui.Dummy(new Vector2(10, 10));
            }

            if (ImGui.Button("取消###qrCancel"))
            {
                Cancelled = true;
                // CloseDialog();
            }
        }

        ImGui.EndChild();

        ImGui.PopStyleVar();

        base.Draw();
    }
}