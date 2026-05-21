using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.IO;
using System.Text.Json;

namespace MicBoost
{
    public class MainForm : Form
    {
        // ── Audio ────────────────────────────────────────────────────
        private WasapiCapture? _capture;
        private WasapiOut?     _output;
        private BufferedWaveProvider? _buffer;
        private bool  _running = false;
        private float _gain    = 3.0f;
        private float _level   = 0f;
        private bool  _clip    = false;

        // ── Controls ─────────────────────────────────────────────────
        private ComboBox cmbInput  = new();
        private ComboBox cmbOutput = new();
        private TrackBar trkGain   = new();
        private Label    lblGain   = new();
        private Button   btnStart  = new();
        private Panel    pnlVU     = new();
        private Label    lblClip   = new();
        private NotifyIcon? _trayIcon;
        private System.Windows.Forms.Timer _vuTimer = new();

        // ── Device lists ─────────────────────────────────────────────
        private List<MMDevice> _inDevices  = new();
        private List<MMDevice> _outDevices = new();
        private static readonly string SettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "MicBoost", "settings.json");

        // ── Colors ───────────────────────────────────────────────────
        static readonly Color BG      = Color.FromArgb(15,  15,  19);
        static readonly Color PANEL   = Color.FromArgb(26,  26,  36);
        static readonly Color ACCENT  = Color.FromArgb(0,   229, 255);
        static readonly Color ACCENT2 = Color.FromArgb(255, 60,  110);
        static readonly Color TEXT    = Color.FromArgb(232, 234, 240);
        static readonly Color MUTED   = Color.FromArgb(85,  85,  112);
        static readonly Color GREEN   = Color.FromArgb(0,   230, 118);
        static readonly Color YELLOW  = Color.FromArgb(255, 234, 0);
        static readonly Color RED     = Color.FromArgb(255, 23,  68);

        public MainForm()
        {
            InitializeComponent();
            LoadDevices();
            LoadSettings();
            SetupTray();

            // Автостарт + свернуть в трей
            var args = Environment.GetCommandLineArgs();
            bool autostart = args.Length > 1 && args[1] == "--autostart";
            if (autostart)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
                var startTimer = new System.Windows.Forms.Timer { Interval = 3000 };
                startTimer.Tick += (s, e) =>
                {
                    startTimer.Stop();
                    startTimer.Dispose();
                    LoadDevices();   // перечитать устройства — VB-CABLE уже активен
                    Hide();
                    StartAudio();
                };
                startTimer.Start();
            }
            _vuTimer.Interval = 40;
            _vuTimer.Tick += (s, e) => pnlVU.Invalidate();
            _vuTimer.Start();
        }

        // ─────────────────────────────────────────────────────────────
        //  UI
        // ─────────────────────────────────────────────────────────────
        private void InitializeComponent()
        {
            Text            = "MicBoost";
            Size            = new Size(440, 540);
            MinimumSize     = new Size(440, 540);
            MaximumSize     = new Size(440, 540);
            BackColor       = BG;
            ForeColor       = TEXT;
            Font            = new Font("Consolas", 9f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;

            var lblTitle = new Label
            {
                Text      = "◈ MicBoost",
                Font      = new Font("Consolas", 18f, FontStyle.Bold),
                ForeColor = ACCENT,
                BackColor = BG,
                Location  = new Point(18, 18),
                AutoSize  = true
            };

            var lblDot = new Label
            {
                Text      = "●",
                Font      = new Font("Consolas", 16f),
                ForeColor = MUTED,
                BackColor = BG,
                Location  = new Point(390, 22),
                AutoSize  = true,
                Name      = "lblDot"
            };

            var sep = new Panel
            {
                Location  = new Point(18, 58),
                Size      = new Size(390, 1),
                BackColor = MUTED
            };

            // ── Device panel ──
            var pnlDev = new Panel
            {
                Location  = new Point(18, 68),
                Size      = new Size(390, 145),
                BackColor = PANEL
            };

            var lblIn  = MakeLabel("INPUT  (USB Microphone)",            12, 10,  MUTED);
            cmbInput   = MakeCombo(12, 28);
            var lblOut = MakeLabel("OUTPUT  (VB-CABLE / Virtual device)", 12, 72,  MUTED);
            cmbOutput  = MakeCombo(12, 90);

            var btnRefresh = new Button
            {
                Text      = "↻ Refresh",
                Font      = new Font("Consolas", 8f),
                ForeColor = MUTED,
                BackColor = PANEL,
                FlatStyle = FlatStyle.Flat,
                Location  = new Point(300, 120),
                Size      = new Size(78, 22),
                Cursor    = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.Click += (s, e) => LoadDevices();
            pnlDev.Controls.AddRange(new Control[] { lblIn, cmbInput, lblOut, cmbOutput, btnRefresh });

            // ── Gain ──
            var lblGainHdr = MakeLabel("GAIN", 18, 230, MUTED);

            trkGain = new TrackBar
            {
                Minimum       = 10,
                Maximum       = 100,
                Value         = 30,
                TickFrequency = 10,
                Location      = new Point(18, 248),
                Size          = new Size(260, 36),
                BackColor     = BG
            };
            trkGain.Scroll += OnGainScroll;

            lblGain = new Label
            {
                Text      = "×3.0  (+9.5 dB)",
                Font      = new Font("Consolas", 12f, FontStyle.Bold),
                ForeColor = ACCENT,
                BackColor = BG,
                Location  = new Point(282, 252),
                Size      = new Size(148, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };

            // ── VU meter ──
            var lblLevel = MakeLabel("LEVEL", 18, 300, MUTED);

            pnlVU = new Panel
            {
                Location  = new Point(18, 318),
                Size      = new Size(390, 22),
                BackColor = PANEL
            };
            pnlVU.Paint += OnVUPaint;

            lblClip = new Label
            {
                Text      = "",
                ForeColor = RED,
                BackColor = BG,
                Location  = new Point(360, 342),
                Size      = new Size(48, 16),
                TextAlign = ContentAlignment.MiddleRight
            };

            // ── Start/Stop ──
            btnStart = new Button
            {
                Text      = "▶  START",
                Font      = new Font("Consolas", 11f, FontStyle.Bold),
                ForeColor = BG,
                BackColor = ACCENT,
                FlatStyle = FlatStyle.Flat,
                Size      = new Size(160, 44),
                Cursor    = Cursors.Hand
            };
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.Location = new Point((440 - 160) / 2, 378);
            btnStart.Click += OnToggle;

            // ── Footer ──
            var lblFooter1 = MakeLabel("Route: USB Mic → MicBoost → VB-CABLE → Apps", 18, 440, MUTED);
            var lblFooter2 = MakeLabel("In apps: select  CABLE Output  as microphone",  18, 458, MUTED);

            Controls.AddRange(new Control[]
            {
                lblTitle, lblDot, sep, pnlDev,
                lblGainHdr, trkGain, lblGain,
                lblLevel, pnlVU, lblClip,
                btnStart, lblFooter1, lblFooter2
            });
        }

        private Label MakeLabel(string text, int x, int y, Color color) => new Label
        {
            Text      = text,
            ForeColor = color,
            BackColor = Color.Transparent,
            Location  = new Point(x, y),
            AutoSize  = true
        };

        private ComboBox MakeCombo(int x, int y) => new ComboBox
        {
            Location      = new Point(x, y),
            Size          = new Size(366, 22),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor     = BG,
            ForeColor     = TEXT,
            FlatStyle     = FlatStyle.Flat,
            Font          = new Font("Consolas", 8.5f)
        };

        // ─────────────────────────────────────────────────────────────
        //  Devices via WASAPI MMDeviceEnumerator
        // ─────────────────────────────────────────────────────────────
        private void LoadDevices()
        {
            // dispose old device references
            foreach (var d in _inDevices)  try { d.Dispose(); } catch { }
            foreach (var d in _outDevices) try { d.Dispose(); } catch { }
            _inDevices.Clear();
            _outDevices.Clear();
            cmbInput.Items.Clear();
            cmbOutput.Items.Clear();

            var enumerator = new MMDeviceEnumerator();

            // Inputs
            var inColl = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var seenIn = new HashSet<string>();
            foreach (var dev in inColl)
            {
                var name = dev.FriendlyName;
                if (seenIn.Contains(name)) { dev.Dispose(); continue; }
                seenIn.Add(name);
                _inDevices.Add(dev);
                cmbInput.Items.Add(name);
            }

            // Outputs
            var outColl = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var seenOut = new HashSet<string>();
            foreach (var dev in outColl)
            {
                var name = dev.FriendlyName;
                if (seenOut.Contains(name)) { dev.Dispose(); continue; }
                seenOut.Add(name);
                _outDevices.Add(dev);
                cmbOutput.Items.Add(name);
            }

            // Auto-select USB mic
            for (int i = 0; i < cmbInput.Items.Count; i++)
            {
                var n = cmbInput.Items[i]!.ToString()!.ToLower();
                if (n.Contains("usb") || n.Contains("pnp"))
                { cmbInput.SelectedIndex = i; break; }
            }
            if (cmbInput.SelectedIndex < 0 && cmbInput.Items.Count > 0)
                cmbInput.SelectedIndex = 0;

            // Auto-select CABLE Input
            for (int i = 0; i < cmbOutput.Items.Count; i++)
            {
                var n = cmbOutput.Items[i]!.ToString()!.ToLower();
                if (n.Contains("cable input"))
                { cmbOutput.SelectedIndex = i; break; }
            }
            if (cmbOutput.SelectedIndex < 0 && cmbOutput.Items.Count > 0)
                cmbOutput.SelectedIndex = 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  Audio engine — WASAPI Shared callback
        // ─────────────────────────────────────────────────────────────
        private void OnToggle(object? s, EventArgs e)
        {
            if (!_running) StartAudio();
            else           StopAudio();
        }

        private void StartAudio()
        {
            int inIdx  = cmbInput.SelectedIndex;
            int outIdx = cmbOutput.SelectedIndex;
            if (inIdx < 0 || outIdx < 0)
            {
                MessageBox.Show("Выбери входное и выходное устройство.", "MicBoost");
                return;
            }

            var inDev  = _inDevices[inIdx];
            var outDev = _outDevices[outIdx];

            // ── Фиксируем уровень USB mic = 100% ──
            try
            {
                inDev.AudioEndpointVolume.MasterVolumeLevelScalar = 1.0f;
                inDev.AudioEndpointVolume.Mute = false;
            }
            catch { /* не критично */ }

            // ── Фиксируем уровень CABLE Output (устройство захвата) = 100% ──
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var capDevs = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var dev in capDevs)
                {
                    if (dev.FriendlyName.ToLower().Contains("cable output"))
                    {
                        dev.AudioEndpointVolume.MasterVolumeLevelScalar = 1.0f;
                        dev.AudioEndpointVolume.Mute = false;
                        dev.Dispose();
                        break;
                    }
                    dev.Dispose();
                }
            }
            catch { /* не критично */ }

            try
            {
                // WASAPI Shared capture — использует родной формат устройства
                _capture = new WasapiCapture(inDev, true, 20);
                var capFmt = _capture.WaveFormat;

                // конвертируем в PCM 16-bit если нужно (WasapiCapture может вернуть float)
                WaveFormat outFmt;
                if (capFmt.Encoding == WaveFormatEncoding.IeeeFloat)
                    outFmt = new WaveFormat(capFmt.SampleRate, 16, capFmt.Channels);
                else
                    outFmt = capFmt;

                _buffer = new BufferedWaveProvider(outFmt)
                {
                    BufferDuration          = TimeSpan.FromMilliseconds(500),
                    DiscardOnBufferOverflow = true
                };

                _capture.DataAvailable += (s, e) => OnData(e, capFmt, outFmt);

                // WASAPI Shared output
                _output = new WasapiOut(outDev, AudioClientShareMode.Shared, true, 20);
                _output.Init(_buffer);

                _capture.StartRecording();
                _output.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска:\n{ex.Message}", "MicBoost");
                StopAudio();
                return;
            }

            _running = true;
            btnStart.Text      = "■  STOP";
            btnStart.BackColor = ACCENT2;
            btnStart.ForeColor = Color.White;
            cmbInput.Enabled   = false;
            cmbOutput.Enabled  = false;
            SetDot(GREEN);
        }

        private void OnData(WaveInEventArgs e, WaveFormat capFmt, WaveFormat outFmt)
        {
            var src   = e.Buffer;
            int bytes = e.BytesRecorded;
            if (bytes == 0) return;

            byte[] dst;

            if (capFmt.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                // float32 → apply gain → convert to int16
                int samples = bytes / 4;
                dst = new byte[samples * 2];
                float sumSq = 0f;
                bool  clip  = false;

                for (int i = 0; i < samples; i++)
                {
                    float f = BitConverter.ToSingle(src, i * 4) * _gain;
                    if (f >  1f) { f =  1f; clip = true; }
                    if (f < -1f) { f = -1f; clip = true; }
                    sumSq += f * f;
                    short s16 = (short)(f * 32767f);
                    dst[i * 2]     = (byte)(s16 & 0xFF);
                    dst[i * 2 + 1] = (byte)(s16 >> 8);
                }
                _level = MathF.Min(MathF.Sqrt(sumSq / samples) * 4f, 1f);
                _clip  = clip;
            }
            else
            {
                // int16 → apply gain → int16
                int samples = bytes / 2;
                dst = new byte[bytes];
                float sumSq = 0f;
                bool  clip  = false;

                for (int i = 0; i < samples; i++)
                {
                    float f = BitConverter.ToInt16(src, i * 2) / 32768f * _gain;
                    if (f >  1f) { f =  1f; clip = true; }
                    if (f < -1f) { f = -1f; clip = true; }
                    sumSq += f * f;
                    short s16 = (short)(f * 32767f);
                    dst[i * 2]     = (byte)(s16 & 0xFF);
                    dst[i * 2 + 1] = (byte)(s16 >> 8);
                }
                _level = MathF.Min(MathF.Sqrt(sumSq / samples) * 4f, 1f);
                _clip  = clip;
            }

            _buffer?.AddSamples(dst, 0, dst.Length);
            BeginInvoke(() => lblClip.Text = _clip ? "⚠ CLIP" : "");
        }

        private void StopAudio()
        {
            _capture?.StopRecording();
            _capture?.Dispose();
            _output?.Stop();
            _output?.Dispose();
            _capture = null;
            _output  = null;
            _buffer  = null;
            _level   = 0;
            _clip    = false;
            _running = false;

            btnStart.Text      = "▶  START";
            btnStart.BackColor = ACCENT;
            btnStart.ForeColor = BG;
            cmbInput.Enabled   = true;
            cmbOutput.Enabled  = true;
            lblClip.Text       = "";
            SetDot(MUTED);
        }

        // ─────────────────────────────────────────────────────────────
        //  Gain
        // ─────────────────────────────────────────────────────────────
        private void OnGainScroll(object? s, EventArgs e)
        {
            _gain = trkGain.Value / 10f;
            double db = 20 * Math.Log10(_gain);
            lblGain.Text = $"×{_gain:F1}  ({db:+0.0;-0.0} dB)";
        }

        // ─────────────────────────────────────────────────────────────
        //  VU meter
        // ─────────────────────────────────────────────────────────────
        private void OnVUPaint(object? s, PaintEventArgs e)
        {
            int w    = pnlVU.Width;
            int h    = pnlVU.Height;
            int fill = (int)(w * _level);
            Color barColor = _level > 0.85f ? RED : _level > 0.60f ? YELLOW : GREEN;
            e.Graphics.Clear(PANEL);
            if (fill > 0)
                using (var br = new SolidBrush(barColor))
                    e.Graphics.FillRectangle(br, 0, 0, fill, h);
        }

        // ─────────────────────────────────────────────────────────────
        //  Tray
        // ─────────────────────────────────────────────────────────────
        private void SetupTray()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var br  = new SolidBrush(ACCENT);
                using var br2 = new SolidBrush(BG);
                g.FillEllipse(br,  1, 1, 14, 14);
                g.FillRectangle(br2, 6, 3, 4, 7);
                g.FillEllipse(br2, 4, 8, 8, 4);
                g.FillRectangle(br2, 7, 12, 2, 3);
            }

            _trayIcon = new NotifyIcon
            {
                Icon    = Icon.FromHandle(bmp.GetHicon()),
                Text    = "MicBoost",
                Visible = true
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Показать", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("Выход",    null, (s, e) => { StopAudio(); _trayIcon.Visible = false; Application.Exit(); });
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick     += (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        }

        private void SetDot(Color c)
        {
            if (Controls["lblDot"] is Label dot) dot.ForeColor = c;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            { e.Cancel = true; Hide(); return; }
            SaveSettings();
            StopAudio();
            if (_trayIcon != null) _trayIcon.Visible = false;
            base.OnFormClosing(e);
        }

        private void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                var obj = new { gain = trkGain.Value };
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(obj));
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                var json = File.ReadAllText(SettingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("gain", out var g))
                {
                    int val = Math.Clamp(g.GetInt32(), trkGain.Minimum, trkGain.Maximum);
                    trkGain.Value = val;
                    OnGainScroll(null, EventArgs.Empty);
                }
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var d in _inDevices)  try { d.Dispose(); } catch { }
                foreach (var d in _outDevices) try { d.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
