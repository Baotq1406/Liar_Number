using System;
using System.Collections.Generic;
using UnityEngine;

public class MessageDispatcher
{
    // Luu cac handler theo type cua message
    private readonly Dictionary<string, Action<string>> _handlers = new Dictionary<string, Action<string>>(StringComparer.OrdinalIgnoreCase);

    // Dang ky handler cho mot loai message
    public void RegisterHandler(string type, Action<string> handler)
    {
        if (string.IsNullOrEmpty(type) || handler == null)
        {
            Debug.LogWarning("[MessageDispatcher] Type hoac handler null");
            return;
        }

        if (_handlers.ContainsKey(type))
        {
            // Neu da co handler, them vao (multicast)
            _handlers[type] += handler;
        }
        else
        {
            // Tao moi
            _handlers[type] = handler;
        }

        Debug.Log("[MessageDispatcher] Da dang ky handler cho type: " + type);
    }

    // Huy dang ky handler
    public void UnregisterHandler(string type, Action<string> handler)
    {
        if (!_handlers.ContainsKey(type)) return;

        _handlers[type] -= handler;
        
        // Neu khong con handler nao thi xoa key
        if (_handlers[type] == null)
        {
            _handlers.Remove(type);
        }

        Debug.Log("[MessageDispatcher] Da huy dang ky handler cho type: " + type);
    }

    // Xu ly message json tu server
    public void Dispatch(string jsonLine)
    {
        try
        {
            // Parse JSON thanh NetworkMessage
            var msg = JsonUtility.FromJson<NetworkMessage>(jsonLine);
            
            if (msg == null || string.IsNullOrEmpty(msg.type))
            {
                Debug.LogWarning("[MessageDispatcher] Message khong hop le: " + jsonLine);
                return;
            }

            var type = msg.type.Trim();
            if (string.IsNullOrEmpty(type))
            {
                Debug.LogWarning("[MessageDispatcher] Message type rong: " + jsonLine);
                return;
            }

            Debug.Log("[MessageDispatcher] Nhan message type: " + type);

            // Tim handler tuong ung va goi
            if (_handlers.TryGetValue(type, out var handler))
            {
                handler.Invoke(msg.payload);
            }
            else
            {
                Debug.LogWarning("[MessageDispatcher] Khong co handler cho type: " + type);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[MessageDispatcher] Loi dispatch: " + e.Message + " | JSON: " + jsonLine);
        }
    }
}
