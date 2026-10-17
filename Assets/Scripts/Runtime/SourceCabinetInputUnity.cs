using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Blitz.AssetImport;
using Blitz.Contracts;
using UnityEngine;

namespace Blitz.Runtime
{
    public sealed class SourceCabinetUnityInput
    {
        private readonly SourceMatchConfiguration configuration;
        private readonly SourceCabinetQuantizer quantizer;
        private readonly SourceCabinetInputBuffer buffer;
#if !UNITY_WEBGL || UNITY_EDITOR
        private readonly bool nativeGamepads;
#endif
        private readonly HashSet<KeyCode> keyboard = new HashSet<KeyCode>();
        private readonly SourceButtons[] softwareButtons = new SourceButtons[4];
        private readonly SourceCabinetSample[] samples;
        private static readonly KeyCode[,] Keys = {
            { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.V, KeyCode.B, KeyCode.N },
            { KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Delete, KeyCode.End, KeyCode.PageDown },
            { KeyCode.I, KeyCode.K, KeyCode.J, KeyCode.L, KeyCode.U, KeyCode.O, KeyCode.P },
            { KeyCode.T, KeyCode.G, KeyCode.F, KeyCode.H, KeyCode.Z, KeyCode.X, KeyCode.C }
        };
#if !UNITY_WEBGL || UNITY_EDITOR
        private static readonly KeyCode[,] PadButtons = {
            { KeyCode.Joystick1Button0, KeyCode.Joystick1Button1, KeyCode.Joystick1Button5 },
            { KeyCode.Joystick2Button0, KeyCode.Joystick2Button1, KeyCode.Joystick2Button5 },
            { KeyCode.Joystick3Button0, KeyCode.Joystick3Button1, KeyCode.Joystick3Button5 },
            { KeyCode.Joystick4Button0, KeyCode.Joystick4Button1, KeyCode.Joystick4Button5 }
        };
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern double SourceCabinetGamepad(int station, int metric);
#endif
        public SourceCabinetInputBuffer Buffer { get { return buffer; } }

        public SourceCabinetUnityInput(SourceMatchConfiguration configuration, SourceInterfaceInputTables tables, bool nativeGamepadsEnabled)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            quantizer = new SourceCabinetQuantizer(tables);
            buffer = new SourceCabinetInputBuffer(configuration);
            samples = new SourceCabinetSample[configuration.Stations.Count];
#if !UNITY_WEBGL || UNITY_EDITOR
            nativeGamepads = nativeGamepadsEnabled;
#endif
        }

        public void Poll(SourceMatchView match)
        {
            foreach (SourceStationConfiguration station in configuration.Stations)
            {
                SourceCabinetStations.View(match, station.StationId);
                for (int key = 0; key < 7; key++)
                {
                    KeyCode code = Keys[station.StationId, key];
                    if (Input.GetKey(code)) keyboard.Add(code);
                    else keyboard.Remove(code);
                }
            }
            Capture();
        }

        public void KeyboardEvent(Event value)
        {
            if (value == null || value.type != EventType.KeyDown && value.type != EventType.KeyUp) return;
            foreach (SourceStationConfiguration station in configuration.Stations)
                for (int key = 0; key < 7; key++)
                    if (value.keyCode == Keys[station.StationId, key])
                    {
                        if (value.type == EventType.KeyDown) keyboard.Add(value.keyCode);
                        else keyboard.Remove(value.keyCode);
                        Capture();
                        return;
                    }
        }

        public void Pulse(int stationId, SourceButtons button)
        {
            SourceCabinetQuantizer.ValidateStationId(stationId);
            bool configured = false;
            foreach (SourceStationConfiguration station in configuration.Stations)
                if (station.StationId == stationId) { configured = true; break; }
            if (!configured || button != SourceButtons.A && button != SourceButtons.B && button != SourceButtons.C)
                throw new SourceInterfaceException("A browser cabinet button must name an actual configured station and A/B/C.");
            softwareButtons[stationId] |= button; Capture();
            softwareButtons[stationId] &= ~button; Capture();
        }

        public void Clear(bool requireNeutral = true)
        {
            keyboard.Clear(); Array.Clear(softwareButtons, 0, softwareButtons.Length); buffer.Clear(requireNeutral);
        }

        private void Capture()
        {
            for (int i = 0; i < samples.Length; i++)
            {
                int station = configuration.Stations[i].StationId;
                int x = (Held(station, 3) ? 3 : 0) - (Held(station, 2) ? 3 : 0);
                int y = (Held(station, 1) ? 3 : 0) - (Held(station, 0) ? 3 : 0);
                float padX, padY;
                SourceButtons padButtons;
                ReadGamepad(station, out padX, out padY, out padButtons);
                SourceCabinetStick stick = x != 0 || y != 0 ? quantizer.SevenLevel(x, y) : quantizer.Analog(padX, padY);
                SourceButtons buttons = softwareButtons[station] | padButtons;
                if (Held(station, 4)) buttons |= SourceButtons.A;
                if (Held(station, 5)) buttons |= SourceButtons.B;
                if (Held(station, 6)) buttons |= SourceButtons.C;
                samples[i] = new SourceCabinetSample(station, stick, buttons);
            }
            buffer.Capture(samples);
        }

        private bool Held(int station, int key) { return keyboard.Contains(Keys[station, key]); }

        private void ReadGamepad(int station, out float x, out float y, out SourceButtons held)
        {
            x = y = 0; held = SourceButtons.None;
#if UNITY_WEBGL && !UNITY_EDITOR
            double status = SourceCabinetGamepad(station, 0);
            if (status < 0)
                throw new SourceInterfaceException("Gamepad " + (station + 1) + " has no standard mapping; define an explicit cabinet binding rather than guessing buttons.");
            if (status == 0) return;
            x = (float)SourceCabinetGamepad(station, 1); y = (float)SourceCabinetGamepad(station, 2);
            double buttons = SourceCabinetGamepad(station, 3);
            if (buttons < 0 || buttons > 7 || Math.Truncate(buttons) != buttons)
                throw new SourceInterfaceException("The browser returned an invalid raw A/B/C gamepad mask.");
            held = (SourceButtons)(int)buttons;
#else
            if (!nativeGamepads) return;
            string[] names = Input.GetJoystickNames();
            if (station >= names.Length || string.IsNullOrEmpty(names[station])) return;
            x = Input.GetAxisRaw("SourceStation" + (station + 1) + "X");
            y = Input.GetAxisRaw("SourceStation" + (station + 1) + "Y");
            if (Input.GetKey(PadButtons[station, 0])) held |= SourceButtons.A;
            if (Input.GetKey(PadButtons[station, 1])) held |= SourceButtons.B;
            if (Input.GetKey(PadButtons[station, 2])) held |= SourceButtons.C;
#endif
        }
    }
}
