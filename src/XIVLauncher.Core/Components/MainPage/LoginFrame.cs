using System.Numerics;
using ImGuiNET;
using XIVLauncher.Common.Game;
using XIVLauncher.Core.Accounts.Secrets.Providers;
using XIVLauncher.Core.Components.Common;

namespace XIVLauncher.Core.Components.MainPage;

public class LoginFrame : Component
{
    private readonly MainPage mainPage;

    private readonly Input loginInput;
    private readonly Combo areaCombo;
    // private readonly Input passwordInput;
    // private readonly Checkbox oneTimePasswordCheckbox;
    // private readonly Checkbox useSteamServiceCheckbox;
    private readonly Checkbox fastLoginCheckbox;
    private readonly Button loginButton;
    public SdoArea[] SdoAreas = Array.Empty<SdoArea>();

    public string Username
    {
        get => this.loginInput.Value;
        set => this.loginInput.Value = value;
    }
    
    public SdoArea? Area => SdoAreas.FirstOrDefault(area => area.AreaName == this.areaCombo.Value);

    public string Password
    {
        get => String.Empty;
        set
        {
        }
    }

    public bool IsOtp
    {
        get => false;
        set
        {
        }
    }

    public bool IsSteam
    {
        get => false;
        set
        {
        }
    }

    public bool IsAutoLogin
    {
        get => this.fastLoginCheckbox.Value;
        set => this.fastLoginCheckbox.Value = value;
    }

    public event Action<LoginAction>? OnLogin;

    private const string POPUP_ID_LOGINACTION = "popup_loginaction";

    public LoginFrame(MainPage mainPage)
    {
        this.mainPage = mainPage;
        this.areaCombo = new Combo("大区", SdoAreas.Select(area => area.AreaName).ToArray());
        this.loginInput = new Input("盛趣账号", "请输入盛趣账号", new Vector2(12f, 0f), 128);
        // this.passwordInput = new Input("Password", "Enter your password", new Vector2(12f, 0f), 128, flags: ImGuiInputTextFlags.Password | ImGuiInputTextFlags.NoUndoRedo);

        // this.loginInput = new Input("Username", "Enter your Username", new Vector2(12f, 0f), 128);
        // this.passwordInput = new Input("Password", "Enter your password", new Vector2(12f, 0f), 128, flags: ImGuiInputTextFlags.Password | ImGuiInputTextFlags.NoUndoRedo);

        // this.oneTimePasswordCheckbox = new Checkbox("Use one-time password");

        // this.useSteamServiceCheckbox = new Checkbox("Use steam service");

        this.fastLoginCheckbox = new Checkbox("快速登陆");

        this.loginButton = new Button("登陆");
        this.loginButton.Click += () => { this.OnLogin?.Invoke(LoginAction.Game); };
        
        this.ReloadAreas();
    }
    
    public void ReloadAreas()
    {
        Task.Run(async () =>
        {
            try
            {
                SdoAreas = await SdoArea.Get();
            }
            catch (Exception ex)
            {
                SdoAreas = new SdoArea[1] { new SdoArea { AreaName = "服务器状态异常", Areaid = "-1" } };
                throw ex;
            }
            finally
            {
                this.areaCombo.Items = SdoAreas.Select(area => area.AreaName).ToArray();
            }
        });
    }

    private Vector2 GetSize()
    {
        var vp = ImGuiHelpers.ViewportSize;
        return new Vector2(-1, vp.Y - 128f);
    }

    public override void Draw()
    {
        if (ImGui.BeginChild("###loginFrame", this.GetSize()))
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(32f, 32f));
            this.areaCombo.Draw();
            this.loginInput.Draw();
            // this.passwordInput.Draw();

            // this.oneTimePasswordCheckbox.Draw();
            // this.useSteamServiceCheckbox.Draw();
            this.fastLoginCheckbox.Draw();

            ImGui.Dummy(new Vector2(10));

            this.loginButton.Draw();

            ImGui.PopStyleVar();

            ImGui.NewLine();

            ImGui.OpenPopupOnItemClick(POPUP_ID_LOGINACTION, ImGuiPopupFlags.MouseButtonRight);

            ImGui.PushStyleColor(ImGuiCol.PopupBg, ImGuiColors.BlueShade1);

            if (ImGui.BeginPopupContextItem(POPUP_ID_LOGINACTION))
            {
                if (ImGui.MenuItem("Launch without Dalamud"))
                {
                    this.OnLogin?.Invoke(LoginAction.GameNoDalamud);
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Patch without launching"))
                {
                    this.OnLogin?.Invoke(LoginAction.GameNoLaunch);
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Repair game files"))
                {
                    this.OnLogin?.Invoke(LoginAction.Repair);
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Force QR"))
                {
                    this.OnLogin?.Invoke(LoginAction.ForceQR);
                }

                if (LauncherApp.IsDebug)
                {
                    ImGui.Separator();

                    if (ImGui.MenuItem("Fake Login"))
                    {
                        this.OnLogin?.Invoke(LoginAction.Fake);
                    }
                }

                ImGui.EndPopup();
            }

            ImGui.PopStyleColor();

            if (Program.Secrets is DummySecretProvider)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                ImGui.TextWrapped("Take care! No secrets provider is installed or configured. Passwords can't be saved.");
                ImGui.PopStyleColor();

                ImGui.Dummy(new Vector2(15));
            }

            ImGui.PushFont(FontManager.IconFont);

            var extraButtonSize = new Vector2(45) * ImGuiHelpers.GlobalScale;

            if (ImGui.Button(FontAwesomeIcon.CaretDown.ToIconString(), extraButtonSize))
            {
                ImGui.OpenPopup(POPUP_ID_LOGINACTION);
            }

            ImGui.SameLine();

            if (ImGui.Button(FontAwesomeIcon.UserFriends.ToIconString(), extraButtonSize))
            {
                this.mainPage.AccountSwitcher.Open();
            }

            ImGui.PopFont();
        }

        ImGui.EndChild();

        base.Draw();
    }
}