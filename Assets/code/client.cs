﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;

public static class client
{
    /// <summary> How many milliseconds to wait when attempting to connect. </summary>
    public const int CONNECTION_TIMEOUT_MS = 2000;

    // STATE VARIABLES //

    static int last_local_id;
    static float last_ping;
    static KeyValuePair<int, float> last_heartbeat;
    static network_utils.traffic_monitor traffic_up;
    static network_utils.traffic_monitor traffic_down;
    static Queue<pending_message> message_queue;
    static Queue<pending_creation_message> pending_creation_messages;
    static disconnect_func on_disconnect;
    static TcpClient tcp;
    static int last_server_time;
    static int last_server_time_local;

    /// <summary> Any bytes of a partial message from the server that appeared at the
    /// end of a buffer, to be glued to the start of the next buffer. </summary>
    static byte[] truncated_read_message;

    // END STATE VARIABLES //

    public static int server_time => ((int)Time.realtimeSinceStartup - last_server_time_local) + last_server_time;

    /// <summary> Called when the client disconnects. </summary>
    /// <param name="message">The message from the server, if it 
    /// caused the disconnect, null otherwise.</param>
    public delegate void disconnect_func(string message);

    /// <summary> A message waiting to be sent. </summary>
    struct pending_message
    {
        public byte[] bytes;
        public float time_sent;
    }

    /// <summary> A message about creation waiting to be sent. </summary>
    struct pending_creation_message
    {
        public int parent_id;
        public string prefab;
        public networked creating;
    }

    /// <summary> Is the client connected to a server. </summary>
    public static bool connected { get => tcp != null; }

    /// <summary> Create a networked object on the client. Automatically lets the
    /// server know, which will syncronise the object (including it's existance)
    /// on other clients. </summary>
    /// <param name="position">The position to create the object at.</param>
    /// <param name="prefab">The prefab to create.</param>
    /// <param name="parent">The parent of the object to crreate</param>
    /// <param name="rotation">The rotation to create the object with.</param>
    /// <param name="network_id">The network id to create the object with. Negative values
    /// will be assigned a unique negative local id and await a network wide id (this covers
    /// most use cases). Positive values should only be used if we are sure that they 
    /// already exist on the server and should be associated with the object that we 
    /// are creating. </param>
    /// <returns>The created networked object.</returns>
    public static networked create(
        Vector3 position,
        string prefab,
        networked parent = null,
        Quaternion rotation = default,
        int network_id = -1)
    {
        // Instantiate the local object, but keep the name
        var created = networked.look_up(prefab);
        string name = created.name;
        created = Object.Instantiate(created);
        created.name = name;

        if (network_id < 0)
        {
            // Assign a (negative) unique local id
            created.network_id = --last_local_id;
        }
        else
        {
            // Copy the given network id
            created.network_id = network_id;
        }
        created.init_network_variables();

        // Parent if requested
        if (parent != null)
            created.transform.SetParent(parent.transform);

        // Create the object with the desired position + rotation
        if (rotation.Equals(default)) rotation = Quaternion.identity;
        created.networked_position = position;
        created.transform.rotation = rotation;

        // Get the id of my parent
        int parent_id = 0;
        if (parent != null) parent_id = parent.network_id;
        if (parent_id < 0) throw new System.Exception("Cannot create children of unregistered objects!");

        // Queue the creation message before on_create is called, so that 
        // we can safely create children in the on_create method (ensure
        // child creation messaage will arrive after my creation message).
        pending_creation_messages.Enqueue(new pending_creation_message
        {
            creating = created,
            parent_id = parent_id,
            prefab = prefab
        });

        // This is being created by this client
        // so must be the first time/it must have
        // authority
        created.on_first_create();
        created.on_create();

        if (parent != null) parent.on_add_networked_child(created);
        return created;
    }

    /// <summary> Create an object as instructred by a server message, stored in the given buffer. </summary>
    static void create_from_network(byte[] buffer, int offset, int length)
    {
        // Record where the end of the serialization is
        int end = offset + length;

        // Deserialize info needed to reproduce the object
        int network_id = network_utils.decode_int(buffer, ref offset);
        int parent_id = network_utils.decode_int(buffer, ref offset);
        string prefab = network_utils.decode_string(buffer, ref offset);

        // Find the requested parent
        networked parent = parent_id > 0 ? networked.find_by_id(parent_id) : null;

        // Create the reproduction
        var nw = networked.look_up(prefab);
        string name = nw.name;
        nw = Object.Instantiate(nw);
        nw.transform.SetParent(parent?.transform);
        nw.name = name;
        nw.network_id = network_id;
        nw.init_network_variables();

        // Local rotation is intialized to the identity. If rotation
        // is variable, the user should implement that.
        nw.transform.localRotation = Quaternion.identity;

        // The rest is network variables that need deserializing
        int index = 0;
        while (offset < end)
        {
            int nv_length = network_utils.decode_int(buffer, ref offset);
            nw.variable_update(index, buffer, offset, nv_length);
            offset += nv_length;
            index += 1;
        }

        nw.on_create();

        if (parent != null) parent.on_add_networked_child(nw);
    }

    /// <summary> Send all of the messages currently queued. </summary>
    static void send_queued_messages()
    {
        // The buffer used to send messages
        byte[] send_buffer = new byte[tcp.SendBufferSize];
        int offset = 0;
        var stream = tcp.GetStream();

        // Send the message queue
        while (message_queue.Count > 0)
        {
            var msg = message_queue.Dequeue();

            if (msg.bytes.Length > tcp.SendBufferSize)
                throw new System.Exception("Message too large!");

            if (offset + msg.bytes.Length > send_buffer.Length)
            {
                // Message would overrun buffer, send the
                // buffer and obtain a new one
                traffic_up.log_bytes(offset);
                try
                {
                    stream.Write(send_buffer, 0, offset);
                }
                catch
                {
                    disconnect(false, "Write failed, connection forcibly closed.");
                    return;
                }
                send_buffer = new byte[tcp.SendBufferSize];
                offset = 0;
            }

            // Copy the message into the send buffer
            System.Buffer.BlockCopy(msg.bytes, 0, send_buffer, offset, msg.bytes.Length);
            offset += msg.bytes.Length; // Move to next message
        }

        // Send the buffer
        if (offset > 0)
        {
            traffic_up.log_bytes(offset);
            try
            {
                stream.Write(send_buffer, 0, offset);
            }
            catch
            {
                disconnect(false, "Write failed, connection forcibly closed.");
                return;
            }
        }
    }

    /// <summary> Send the updated serialization of the variable with the 
    /// given index, belonging to the networked object with the given network id. </summary>
    public static void send_variable_update(int network_id, int index, byte[] serialization)
    {
        message_senders[MESSAGE.VARIABLE_UPDATE](network_id, index, serialization);
    }

    /// <summary> Called when the render range of a player changes. </summary>
    public static void on_render_range_change(networked_player player)
    {
        message_senders[MESSAGE.RENDER_RANGE_UPDATE](player);
    }

    /// <summary> Called when an object is deleted on this client, sends the server that info. </summary>
    public static void on_delete(networked deleted, bool response_required)
    {
        message_senders[MESSAGE.DELETE](deleted.network_id, response_required);
    }

    /// <summary> Connect the client to a server. </summary>
    public static bool connect(string host, int port, string username, string password, disconnect_func on_disconnect)
    {
        // Initialize client state
        last_local_id = 0;
        last_ping = 0;
        last_heartbeat = default;
        traffic_up = new network_utils.traffic_monitor();
        traffic_down = new network_utils.traffic_monitor();
        message_queue = new Queue<pending_message>();
        pending_creation_messages = new Queue<pending_creation_message>();
        client.on_disconnect = on_disconnect;
        tcp = new TcpClient();
        var connector = tcp.BeginConnect(host, port, null, null);

        // Connection timeout
        if (!connector.AsyncWaitHandle.WaitOne(CONNECTION_TIMEOUT_MS))
        {
            tcp = null;
            return false;
        }

        // Initialize the networked object static state
        networked.client_initialize();

        // Setup message parsers
        message_parsers = new Dictionary<server.MESSAGE, message_parser>
        {
            [server.MESSAGE.CREATE] = (buffer, offset, length) =>
                create_from_network(buffer, offset, length),

            [server.MESSAGE.FORCE_CREATE] = (buffer, offset, length) =>
            {
                Vector3 position = network_utils.decode_vector3(buffer, ref offset);
                string prefab = network_utils.decode_string(buffer, ref offset);
                int network_id = network_utils.decode_int(buffer, ref offset);
                int parent_id = network_utils.decode_int(buffer, ref offset);

                var created = create(position, prefab,
                    parent: parent_id > 0 ? networked.find_by_id(parent_id) : null,
                    network_id: network_id);
            },

            [server.MESSAGE.CREATION_SUCCESS] = (buffer, offset, length) =>
            {
                // Update the local id to a network-wide one
                int local_id = network_utils.decode_int(buffer, ref offset);
                int network_id = network_utils.decode_int(buffer, ref offset);
                var nw = networked.try_find_by_id(local_id);
                if (nw != null) nw.network_id = network_id;
            },

            [server.MESSAGE.DELETE_SUCCESS] = (buffer, offset, length) =>
            {
                // A delete was succesfully processed on the server
                int network_id = network_utils.decode_int(buffer, ref offset);
                networked.on_delete_success_response(network_id);
            },

            [server.MESSAGE.VARIABLE_UPDATE] = (buffer, offset, length) =>
            {
                // Forward the variable update to the correct object
                int start = offset;
                int id = network_utils.decode_int(buffer, ref offset);
                int index = network_utils.decode_int(buffer, ref offset);
                var nw = networked.try_find_by_id(id);
                nw?.variable_update(index, buffer, offset, length - (offset - start));
            },

            [server.MESSAGE.UNLOAD] = (buffer, offset, length) =>
            {
                // Remove the object from the client
                int id = network_utils.decode_int(buffer, ref offset);
                bool deleting = network_utils.decode_bool(buffer, ref offset);
                var found = networked.try_find_by_id(id);
                found?.forget(deleting);
            },

            [server.MESSAGE.GAIN_AUTH] = (buffer, offset, length) =>
            {
                // Gain authority over a networked object
                int network_id = network_utils.decode_int(buffer, ref offset);
                var nw = networked.try_find_by_id(network_id);
                nw?.gain_authority();
            },

            [server.MESSAGE.LOSE_AUTH] = (buffer, offset, length) =>
            {
                // Loose authority over a networked object
                int network_id = network_utils.decode_int(buffer, ref offset);
                var nw = networked.try_find_by_id(network_id);
                nw?.lose_authority();
            },

            [server.MESSAGE.HEARTBEAT] = (buffer, offset, length) =>
            {
                int heartbeat_key = network_utils.decode_int(buffer, ref offset);
                last_server_time = network_utils.decode_int(buffer, ref offset);
                last_server_time_local = (int)Time.realtimeSinceStartup;

                if (last_heartbeat.Key == heartbeat_key)
                {
                    // Record the ping
                    last_ping = Time.realtimeSinceStartup - last_heartbeat.Value;
                }
                else
                {
                    last_ping = -1;
                    Debug.Log("Heartbeat key mismatch, packet loss/very high ping?");
                }
            },

            [server.MESSAGE.DISCONNECT] = (buffer, offset, length) =>
            {
                // The server told us to disconnect. How rude.
                string msg = network_utils.decode_string(buffer, ref offset);
                disconnect(false, msg_from_server: msg);
            }
        };

        // Send a message type + payload
        void send(MESSAGE msg_type, byte[] payload)
        {
            // Message is of form [length, type, payload]
            byte[] to_send = network_utils.concat_buffers(
                network_utils.encode_int(payload.Length),
                new byte[] { (byte)msg_type },
                payload
            );

            message_queue.Enqueue(new pending_message
            {
                bytes = to_send,
                time_sent = Time.realtimeSinceStartup
            });
        }

        // Setup message senders
        message_senders = new Dictionary<MESSAGE, message_sender>
        {
            [MESSAGE.LOGIN] = (args) =>
            {
                if (args.Length != 2)
                    throw new System.ArgumentException("Wrong number of arguments!");

                string uname = (string)args[0];
                string pword = (string)args[1];

                var hasher = System.Security.Cryptography.SHA256.Create();
                var hashed = hasher.ComputeHash(System.Text.Encoding.ASCII.GetBytes(pword));

                // Send the username + hashed password to the server
                send(MESSAGE.LOGIN, network_utils.concat_buffers(
                    network_utils.encode_string(uname), hashed));
            },

            [MESSAGE.HEARTBEAT] = (args) =>
            {
                if (args.Length != 0)
                    throw new System.Exception("Wrong number of arguments!");

                // Increment the heartbeat key, and record the send time
                last_heartbeat = new KeyValuePair<int, float>(
                    last_heartbeat.Key + 1,
                    Time.realtimeSinceStartup
                );

                send(MESSAGE.HEARTBEAT, network_utils.encode_int(last_heartbeat.Key));
            },

            [MESSAGE.DISCONNECT] = (args) =>
            {
                if (args.Length != 0)
                    throw new System.ArgumentException("Wrong number of arguments!");

                send(MESSAGE.DISCONNECT, new byte[0]);
            },

            [MESSAGE.CREATE] = (args) =>
            {
                if (args.Length != 4)
                    throw new System.ArgumentException("Wrong number of arguments!");

                int local_id = (int)args[0];
                int parent_id = (int)args[1];
                string prefab = (string)args[2];
                byte[] variable_serializations = (byte[])args[3];

                send(MESSAGE.CREATE, network_utils.concat_buffers(
                    network_utils.encode_int(local_id),
                    network_utils.encode_int(parent_id),
                    network_utils.encode_string(prefab),
                    variable_serializations
                ));
            },

            [MESSAGE.DELETE] = (args) =>
            {
                if (args.Length != 2)
                    throw new System.ArgumentException("Wrong number of arguments!");

                int network_id = (int)args[0];
                if (network_id < 0)
                    throw new System.Exception("Tried to delete an unregistered object!");

                bool response_required = (bool)args[1];

                send(MESSAGE.DELETE, network_utils.concat_buffers(
                    network_utils.encode_int(network_id),
                    network_utils.encode_bool(response_required)
                ));
            },

            [MESSAGE.RENDER_RANGE_UPDATE] = (args) =>
            {
                if (args.Length != 1)
                    throw new System.ArgumentException("Wrong number of arguments!");

                var nw = (networked_player)args[0];

                // Send the new render range
                send(MESSAGE.RENDER_RANGE_UPDATE, network_utils.concat_buffers(
                    network_utils.encode_float(nw.render_range)
                ));
            },

            [MESSAGE.VARIABLE_UPDATE] = (args) =>
            {
                if (args.Length != 3)
                    throw new System.ArgumentException("Wrong number of arguments!");

                int network_id = (int)args[0];
                int index = (int)args[1];
                byte[] serialization = (byte[])args[2];

                send(MESSAGE.VARIABLE_UPDATE, network_utils.concat_buffers(
                    network_utils.encode_int(network_id),
                    network_utils.encode_int(index),
                    serialization
                ));
            }
        };

        // Send login message
        message_senders[MESSAGE.LOGIN](username, password);
        return true;
    }

    public static void disconnect(bool initiated_by_client, string msg_from_server = null)
    {
        if (!connected) return; // Not connected

        if (initiated_by_client)
        {
            // Send any queued messages, followed by a disconnect message
            send_queued_messages();
            message_senders[MESSAGE.DISCONNECT]();
        }

        try
        {
            // Close the stream (with a timeout so the above messages can be sent)
            tcp.GetStream().Close((int)(server.CLIENT_TIMEOUT * 1000));
        }
        catch
        {
            Debug.Log("Connection severed ungracefully.");
        }

        tcp.Close();
        tcp = null;

        on_disconnect(msg_from_server);
    }

    public static void update()
    {
        if (!connected) return;

        // Get the tcp stream
        NetworkStream stream = null;
        try
        {
            stream = tcp.GetStream();
        }
        catch (System.InvalidOperationException e)
        {
            disconnect(false, e.Message);
            return;
        }

        // Read messages to the client
        while (stream.DataAvailable)
        {
            byte[] buffer = new byte[tcp.ReceiveBufferSize];

            // The start point in the buffer where new messages should be read into
            int buffer_start = 0;

            if (truncated_read_message != null)
            {
                // Glue a truncated message onto the start of the buffer
                System.Buffer.BlockCopy(truncated_read_message, 0,
                    buffer, 0, truncated_read_message.Length);
                buffer_start = truncated_read_message.Length;
                truncated_read_message = null;
            }

            // Read new bytes into the buffer
            int bytes_read = stream.Read(buffer, buffer_start,
                buffer.Length - buffer_start);
            traffic_down.log_bytes(bytes_read);

            // Work out how much data is in the buffer (including data
            // potentially copied from a previous truncation) and
            // initialze reading at the beginning.
            int data_bytes = bytes_read + buffer_start;
            int offset = 0;

            // Variables for dealing with truncations
            int last_message_start = 0;
            bool truncated = false;

            while (offset < data_bytes)
            {
                // Record the message start, in case we get truncated
                last_message_start = offset;

                // Check the payload length is in the buffer
                if (offset + sizeof(int) > data_bytes)
                {
                    truncated = true;
                    break;
                }

                // Parse payload length
                int payload_length = network_utils.decode_int(buffer, ref offset);

                // Check the whole message is in the buffer
                if (offset + payload_length + 1 > data_bytes)
                {
                    truncated = true;
                    break;
                }

                // Parse message type
                var msg_type = (server.MESSAGE)buffer[offset];
                offset += 1;

                // Handle the message
                message_parsers[msg_type](buffer, offset, payload_length);
                offset += payload_length;

                // The last message parsed caused a disconnect, we
                // should stop immedately as we can no longer send
                // messages to the server.
                if (!connected) return;
            }

            // Save the truncated part of the message, to glue onto the
            // start of the next buffer
            if (truncated)
            {
                // Save the truncated message to be glued at the start of the next buffer.
                truncated_read_message = new byte[data_bytes - last_message_start];
                System.Buffer.BlockCopy(buffer, last_message_start,
                    truncated_read_message, 0, truncated_read_message.Length);
            }
        }

        // Queue pending creation messages (delayed until now
        // so that for each created object we have the most
        // up-to-date network variables).
        while (pending_creation_messages.Count > 0)
        {
            var cm = pending_creation_messages.Dequeue();

            if (cm.creating == null)
                continue; // This object has since been deleted

            // Request creation on the server
            message_senders[MESSAGE.CREATE](cm.creating.network_id,
                cm.parent_id, cm.prefab, cm.creating.serialize_networked_variables());
        }

        // Run network_update for each object
        networked.network_updates();

        // If it's been long enough, send another heartbeat
        if (Time.realtimeSinceStartup - last_heartbeat.Value > server.CLIENT_HEARTBEAT_PERIOD)
            message_senders[MESSAGE.HEARTBEAT]();

        // Send messages
        send_queued_messages();
    }

    public static string info()
    {
        if (!connected) return "Client not connected.";

        var ep = (System.Net.IPEndPoint)tcp.Client.RemoteEndPoint;

        // Convert ping to string
        string ping = (last_ping * 1000) + " ms";
        if (last_ping < 0) ping = "> " + server.CLIENT_HEARTBEAT_PERIOD * 1000 + " ms";

        return "Client connected to " + ep.Address + ":" + ep.Port + "\n" +
               "    Objects            : " + networked.object_count + "\n" +
               "    Recently forgotten : " + networked.recently_forgotten_count + "\n" +
               "    Upload             : " + traffic_up.usage() + "\n" +
               "    Download           : " + traffic_down.usage() + "\n" +
               "    Effective ping     : " + ping + "\n" +
               "    Server time        : " + server_time + " (last = " + last_server_time + ")\n";

    }

    public enum MESSAGE : byte
    {
        // Numbering starts at 1 so erroneous 0's are caught
        LOGIN = 1,           // Client has logged in
        HEARTBEAT,           // Heartbeat response
        DISCONNECT,          // Disconnect this client
        CREATE,              // Create an object on the server
        DELETE,              // Delete an object from the server
        RENDER_RANGE_UPDATE, // Client render range has changed
        VARIABLE_UPDATE,     // A networked_variable has changed
    }

    delegate void message_sender(params object[] args);
    static Dictionary<MESSAGE, message_sender> message_senders;

    delegate void message_parser(byte[] message, int offset, int length);
    static Dictionary<server.MESSAGE, message_parser> message_parsers;
}