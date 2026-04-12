# WPF to Avalonia Migration Notes

## Pixel-Level Differences

### Controls with no exact Avalonia equivalent

1. **Transitioner (MaterialDesignInXAML)** → Carousel (Avalonia)
   - WPF MaterialDesign Transitioner supports FadeWipe/SlideWipe transitions between slides
   - Avalonia Carousel provides similar functionality but transition animations differ
   - Visual difference: transition animation timing and style may not match exactly

2. **PopupBox (MaterialDesignInXAML)** → Button + MenuFlyout
   - WPF PopupBox shows a floating menu with shadow on toggle
   - Avalonia uses MenuFlyout on Button, which has different visual treatment
   - Visual difference: popup position, shadow style, toggle animation

3. **TabablzControl (Dragablz)** → TabControl (Avalonia)
   - Dragablz provided draggable, Material-styled tabs
   - Standard Avalonia TabControl with Material.Avalonia theme used instead
   - Visual difference: tab drag functionality removed; tab styling may differ slightly

4. **DecimalUpDown (Extended.Wpf.Toolkit)** → NumericUpDown (Avalonia)
   - Minor visual difference in spinner button style and sizing

5. **RichTextBox + FlowDocument** → TextBox (read-only)
   - WPF RichTextBox supported rich text with links, paragraphs
   - Replaced with TextBox (IsReadOnly, AcceptsReturn) which is plain text only
   - Visual difference: no inline hyperlink styling in description area of CustomMessageBox

6. **PasswordBox** → TextBox with PasswordChar
   - WPF PasswordBox is a dedicated control with built-in security (SecureString)
   - Avalonia uses TextBox with PasswordChar="●" and RevealPasswordButtonIsVisible
   - Visual difference: reveal button appearance; character masking style

7. **DropShadowEffect** → BoxShadow
   - WPF DropShadowEffect is applied as a bitmap effect on the element
   - Avalonia BoxShadow is CSS-like and renders differently
   - Visual difference: shadow spread, blur radius may not match exactly

### Theme differences

- WPF used MaterialDesignThemes 4.3.0 with explicit dark theme + blue primary/accent
- Avalonia uses Material.Avalonia 3.14.1 with RequestedThemeVariant="Dark"
- Color palette is similar but not identical; some dynamic resources may resolve to slightly different shades

### DataTrigger replacements

- WPF DataTriggers in news item template (MainWindow) and account list (AccountSwitcher) were replaced with:
  - Direct visibility binding via IsVisible
  - Converter-based icon Kind selection (NewsTagConverters.cs)
  - Some triggers may not produce identical visual transitions

### Animations

- OtpInputDialog shake animation: WPF Storyboard → code-based TranslateTransform animation
  - Timing and easing may differ slightly

### Windows-specific features

- JumpList (taskbar shortcuts): removed from Avalonia version (WPF-only feature)
- SystemSounds: removed (System.Media not available in Avalonia context)
- Dalamud overlay STA thread: simplified to run on main UI thread

## Bug Fixes During Migration

1. **QRDialog**: Fixed wrong ViewModel assignment (was `OtpInputDialogViewModel`, now `QRDialogViewModel`)
2. **SettingsControl**: Fixed `VersionLabel.Text` appending on each `ReloadSettings()` call

## Unused Code

- `ChatChannelSetupViewModel.cs` appears orphaned (no matching window references it)
