using MQTTnet;

namespace Amr.Communication.Mqtt;

public enum MqttConnectionFailure
{
    TransportFailure,
    MqttRejected,
    Timeout
}

/// <summary>최초 접속 실패. 사용자 취소는 원래 토큰의 OperationCanceledException으로 구분한다.</summary>
public sealed class MqttConnectionException : Exception
{
    public MqttConnectionException(MqttConnectionFailure reason,
        MqttClientConnectResultCode? resultCode = null, Exception? innerException = null)
        : base($"MQTT connection failed: {reason}.", innerException)
    {
        Reason = reason;
        ResultCode = resultCode;
    }

    public MqttConnectionFailure Reason { get; }
    public MqttClientConnectResultCode? ResultCode { get; }
}
