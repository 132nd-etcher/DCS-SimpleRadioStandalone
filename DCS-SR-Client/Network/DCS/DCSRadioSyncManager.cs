﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.DCSState;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Newtonsoft.Json;
using NLog;

/**
Keeps radio information in Sync Between DCS and

**/

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS
{
    public class DCSRadioSyncManager
    {
        private readonly SendRadioUpdate _clientRadioUpdate;
        private readonly ClientSideUpdate _clientSideUpdate;
        public static readonly string AWACS_RADIOS_FILE = "awacs-radios.json";
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly DCSGameGuiHandler _dcsGameGuiHandler;
        private readonly DCSLineOfSightHandler _lineOfSightHandler;
        private readonly UDPCommandHandler _udpCommandHandler; 
        private readonly DCSRadioSyncHandler _dcsRadioSyncHandler;

        public delegate void ClientSideUpdate();
        public delegate void SendRadioUpdate();

        private volatile bool _stopExternalAWACSMode;

        private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;
        private DispatcherTimer _clearRadio;

        public bool IsListening { get; private set; }

        public DCSRadioSyncManager(SendRadioUpdate clientRadioUpdate, ClientSideUpdate clientSideUpdate,
           string guid, DCSRadioSyncHandler.NewAircraft _newAircraftCallback)
        {
            _clientRadioUpdate = clientRadioUpdate;
            _clientSideUpdate = clientSideUpdate;
            IsListening = false;
            _lineOfSightHandler = new DCSLineOfSightHandler(guid);
            _udpCommandHandler = new UDPCommandHandler();
            _dcsGameGuiHandler = new DCSGameGuiHandler(clientSideUpdate);
            _dcsRadioSyncHandler = new DCSRadioSyncHandler(clientRadioUpdate, _newAircraftCallback);

            _clearRadio = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            _clearRadio.Tick += CheckIfRadioIsStale;
           
        }

        private void CheckIfRadioIsStale(object sender, EventArgs e)
        {
            if (!_clientStateSingleton.DcsPlayerRadioInfo.IsCurrent())
            {
                //check if we've had an update
                if (_clientStateSingleton.DcsPlayerRadioInfo.LastUpdate > 0)
                {
                    _clientStateSingleton.PlayerCoaltionLocationMetadata.Reset();
                    _clientStateSingleton.DcsPlayerRadioInfo.Reset();

                    _clientRadioUpdate();
                    _clientSideUpdate();
                    Logger.Info("Reset Radio state - no longer connected");
                }
            }
        }

        public void Start()
        {
            DcsListener();
            IsListening = true;
        }

        public void StartExternalAWACSModeLoop()
        {
            _stopExternalAWACSMode = false;

            RadioInformation[] awacsRadios;
            try
            {
                string radioJson = File.ReadAllText(AWACS_RADIOS_FILE);
                awacsRadios = JsonConvert.DeserializeObject<RadioInformation[]>(radioJson);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to load AWACS radio file");

                awacsRadios = new RadioInformation[11];
                for (int i = 0; i < 11; i++)
                {
                    awacsRadios[i] = new RadioInformation
                    {
                        freq = 1,
                        freqMin = 1,
                        freqMax = 1,
                        secFreq = 0,
                        modulation = RadioInformation.Modulation.DISABLED,
                        name = "No Radio",
                        freqMode = RadioInformation.FreqMode.COCKPIT,
                        encMode = RadioInformation.EncryptionMode.NO_ENCRYPTION,
                        volMode = RadioInformation.VolumeMode.COCKPIT
                    };
                }
            }

            // Force an immediate update of radio information
            _clientStateSingleton.LastSent = 0;

            Task.Factory.StartNew(() =>
            {
                Logger.Debug("Starting external AWACS mode loop");

                while (!_stopExternalAWACSMode)
                {
                    _dcsRadioSyncHandler.ProcessRadioInfo(new DCSPlayerRadioInfo
                    {
                        LastUpdate = 0,
                        control = DCSPlayerRadioInfo.RadioSwitchControls.HOTAS,
                        name = _clientStateSingleton.LastSeenName,
                        ptt = false,
                        radios = awacsRadios,
                        selected = 1,
                        latLng = new DCSLatLngPosition(){lat =0,lng=0,alt=0},
                        simultaneousTransmission = false,
                        simultaneousTransmissionControl = DCSPlayerRadioInfo.SimultaneousTransmissionControl.ENABLED_INTERNAL_SRS_CONTROLS,
                        unit = "External AWACS",
                        unitId = 100000001,
                        inAircraft = false
                    });

                    Thread.Sleep(1000);
                }

                var radio = new DCSPlayerRadioInfo();
                radio.Reset();
                _dcsRadioSyncHandler.ProcessRadioInfo(radio);

                Logger.Debug("Stopping external AWACS mode loop");
            });
        }

        public void StopExternalAWACSModeLoop()
        {
            _stopExternalAWACSMode = true;
        }

        private void DcsListener()
        {
            _dcsRadioSyncHandler.Start();
            _dcsGameGuiHandler.Start();
            _lineOfSightHandler.Start();
            _udpCommandHandler.Start();
             _clearRadio.Start();
        }

        public void Stop()
        {
            _stopExternalAWACSMode = true;
            IsListening = false;

            _clearRadio.Stop();
            _dcsRadioSyncHandler.Stop();
            _dcsGameGuiHandler.Stop();
            _lineOfSightHandler.Stop();
            _udpCommandHandler.Stop();
            
        }
    }
}