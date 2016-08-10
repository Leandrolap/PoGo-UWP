﻿using System;
using System.Text;
using System.Linq;
using Google.Protobuf;
using POGOProtos.Networking.Signature;
using PokemonGo.RocketAPI.Enums;
using POGOProtos.Networking.Envelopes;
using POGOProtos.Networking.Requests;
using PokemonGo.RocketAPI.Extensions;
using static POGOProtos.Networking.Envelopes.RequestEnvelope.Types;

namespace PokemonGo.RocketAPI.Helpers
{
    public class RequestBuilder
    {
        private readonly string _authToken;
        private readonly AuthType _authType;
        private readonly double _latitude;
        private readonly double _longitude;
        private readonly double _altitude;
        private readonly AuthTicket _authTicket;
        private readonly IDeviceInfo _deviceInfo;
        private readonly DateTime _startTime = DateTime.UtcNow;

        public RequestBuilder(string authToken, AuthType authType, double latitude, double longitude, double altitude, IDeviceInfo deviceInfo,
        AuthTicket authTicket = null)
        {
            _authToken = authToken;
            _authType = authType;
            _latitude = latitude;
            _longitude = longitude;
            _altitude = altitude;
            _authTicket = authTicket;
            _deviceInfo = deviceInfo;
        }

        public RequestEnvelope SetRequestEnvelopeUnknown6(RequestEnvelope requestEnvelope)
        {
            var rnd32 = new byte[32];
            var rnd = new Random();
            rnd.NextBytes(rnd32);

            var ticketBytes = requestEnvelope.AuthTicket.ToByteArray();

            Vector normAccel = Utils.NormalizeVector(_deviceInfo.AccelRawX, _deviceInfo.AccelRawY, _deviceInfo.AngleNormalizedX, 9.81);

            var sig = new Signature()
            {
                LocationHash1 =
                    Utils.GenerateLocation1(ticketBytes, requestEnvelope.Latitude, requestEnvelope.Longitude,
                        requestEnvelope.Altitude),
                LocationHash2 =
                    Utils.GenerateLocation2(requestEnvelope.Latitude, requestEnvelope.Longitude,
                        requestEnvelope.Altitude),
                Unk22 = ByteString.CopyFrom(rnd32),
                Timestamp = (ulong)DateTime.UtcNow.ToUnixTime(),
                TimestampSinceStart = (ulong)(DateTime.UtcNow.ToUnixTime() - _startTime.ToUnixTime()),

                SensorInfo = new Signature.Types.SensorInfo()
                {
                    AccelNormalizedX = normAccel.X,
                    AccelNormalizedY = normAccel.Y,
                    AccelNormalizedZ = normAccel.Z,
                    AccelRawX = -_deviceInfo.AccelRawX,
                    AccelRawY = -_deviceInfo.AccelRawY,
                    AccelRawZ = -_deviceInfo.AccelRawZ,
                    MagnetometerX = _deviceInfo.MagnetometerX,
                    MagnetometerY = _deviceInfo.MagnetometerY,
                    MagnetometerZ = _deviceInfo.MagnetometerZ,
                    GyroscopeRawX = _deviceInfo.GyroscopeRawX,
                    GyroscopeRawY = _deviceInfo.GyroscopeRawY,
                    GyroscopeRawZ = _deviceInfo.GyroscopeRawZ,
                    AngleNormalizedX = _deviceInfo.AngleNormalizedX,
                    AngleNormalizedY = _deviceInfo.AngleNormalizedY,
                    AngleNormalizedZ = _deviceInfo.AngleNormalizedZ,
                    AccelerometerAxes = _deviceInfo.AccelerometerAxes
                    
                },

                DeviceInfo = new Signature.Types.DeviceInfo()
                {
                    DeviceId = _deviceInfo.DeviceID,
                    FirmwareBrand = _deviceInfo.FirmwareBrand,
                    FirmwareType = _deviceInfo.FirmwareType
                }

            };

            _deviceInfo.LocationFixes.ToList().ForEach( loc => sig.LocationFix.Add(new Signature.Types.LocationFix()
            {
                Floor = loc.Floor,
                Longitude = loc.Longitude,
                Latitude = loc.Latitude,
                Altitude = loc.Altitude,
                LocationType = loc.LocationType,
                Provider = loc.Provider,
                ProviderStatus = loc.ProviderStatus
                
            }));

            foreach (var request in requestEnvelope.Requests)
            {
                sig.RequestHash.Add(
                    Utils.GenerateRequestHash(ticketBytes, request.ToByteArray())
                );
            }

            requestEnvelope.Unknown6.Add(new Unknown6()
            {
                RequestType = 6,
                Unknown2 = new Unknown6.Types.Unknown2()
                {
                    Unknown1 = ByteString.CopyFrom(Crypt.Encrypt(sig.ToByteArray()))
                }
            });

            return requestEnvelope;
        }

        public RequestEnvelope GetRequestEnvelope(params Request[] customRequests)
        {
            return SetRequestEnvelopeUnknown6(new RequestEnvelope
            {
                StatusCode = 2, //1

                RequestId = 1469378659230941192, //3
                Requests = { customRequests }, //4

                //Unknown6 = , //6
                Latitude = _latitude, //7
                Longitude = _longitude, //8
                Altitude = _altitude, //9
                AuthTicket = _authTicket, //11
                Unknown12 = 989 //12
            });
        }

        public RequestEnvelope GetInitialRequestEnvelope(params Request[] customRequests)
        {
            return new RequestEnvelope
            {
                StatusCode = 2, //1

                RequestId = 1469378659230941192, //3
                Requests = { customRequests }, //4

                //Unknown6 = , //6
                Latitude = _latitude, //7
                Longitude = _longitude, //8
                Altitude = _altitude, //9
                AuthInfo = new AuthInfo
                {
                    Provider = _authType == AuthType.Google ? "google" : "ptc",
                    Token = new AuthInfo.Types.JWT
                    {
                        Contents = _authToken,
                        Unknown2 = 14
                    }
                }, //10
                Unknown12 = 989 //12
            };
        }

        public RequestEnvelope GetRequestEnvelope(RequestType type, IMessage message)
        {
            return GetRequestEnvelope(new Request()
            {
                RequestType = type,
                RequestMessage = message.ToByteString()
            });

        }
    }
}