﻿using CheapLoc;

namespace XIVLauncher.Windows.ViewModel
{
    class QRDialogViewModel
    {
        public QRDialogViewModel()
        {
            SetupLoc();
        }

        private void SetupLoc()
        {
            OtpInputPromptLoc = Loc.Localize("OtpInputPrompt", "Please enter your OTP key.");
            CancelWithShortcutLoc = Loc.Localize("CancelWithShortcut", "_Cancel");
            OkLoc = Loc.Localize("OK", "OK");
            OtpOneClickHintLoc = Loc.Localize("OtpOneClickHint", "Or use the app!\r\nClick here to learn more!");
            OtpInputPromptBadLoc = Loc.Localize("OtpInputPromptBad", "Enter a valid OTP key.\nIt is 6 digits long.");
        }

        public string OtpInputPromptLoc { get; private set; }
        public string CancelWithShortcutLoc { get; private set; }
        public string OkLoc { get; private set; }
        public string OtpOneClickHintLoc { get; private set; }
        public string OtpInputPromptBadLoc { get; private set; }
    }
}
