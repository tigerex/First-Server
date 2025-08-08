using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Collections.Generic;

public class Server
{
    private const int BUFFER_SIZE = 1024; // Kích thước bộ đệm cho socket
    private const int PORT_NUMBER = 9999; // Port
    private const string IP_ADDRESS = "192.168.2.25"; // Địa chỉ IP của máy chủ
    static ASCIIEncoding encoding = new ASCIIEncoding(); // Sử dụng ASCIIEncoding để mã hóa và giải mã dữ liệu
    private static Dictionary<string, Room> chatRooms = new(); // Thư viện chứa các phòng chat. Lớp Room khai báo phía dưới.
    private static List<User> users = new List<User>(); // Danh sách users
    private static object clientsLock = new object(); // KHông biết cái này để làm gì :D
    static Dictionary<string, string> sessions = new Dictionary<string, string>(); // Lưu trữ phiên làm việc của người dùng
    static Random rand = new Random(); // Tạo số ngẫu nhiên, dùng trong tạo session ID

    // Hàm Main
    public static void Main()
    {
        // Tải người dùng từ file JSON
        string userFilePath = "data/user.json"; // File json có users
        string logFilePath = "data/" + DateTime.Now.ToString("ddMMyyyy") + "_server.log"; // File log server
        // Check coi folder tồn tại chưa đã
        if (File.Exists(userFilePath))
        {
            string json = File.ReadAllText(userFilePath);                                   // Đọc file JSON
            users = System.Text.Json.JsonSerializer.Deserialize<List<User>>(json) ?? new(); // Giải mã JSON thành danh sách người dùng
            Console.WriteLine($"Loaded {users.Count} users.");
        }
        // Chưa thì báo.
        else { Console.WriteLine("User file not found. No users loaded!!!!!!"); }

        // Khởi động Server chỗ này
        try
        {
            IPAddress address = IPAddress.Parse(IP_ADDRESS);                // Chuyển đổi địa chỉ IP từ chuỗi sang IPAddress
            TcpListener listener = new TcpListener(address, PORT_NUMBER);   //9999

            listener.Start();                                               // Bắt đầu lắng nghe kết nối
            File.AppendAllText(logFilePath, $"[{DateTime.Now}] - Server started on {address}:{PORT_NUMBER} successfully!!!\n==========================================================\n"); // Log khởi động thành công
            Console.WriteLine("Server started on " + listener.LocalEndpoint);
            Console.WriteLine("Connect to http://" + address + ":" + PORT_NUMBER);
            Console.WriteLine("\nReady to receive connections...");

            // Đợi kết nối từ client, sử dụng Thread để xử lý nhiều kết nối đồng thời, mỗi kết nối được xử lí riêng.
            while (true)
            {
                Socket socket = listener.AcceptSocket();                        // Chấp nhận kết nối từ client
                Thread clientThread = new Thread(() => HandleClient(socket));   // Tạo Thread mới để xử lý client
                clientThread.Start();                                           // Bắt đầu Thread
                File.AppendAllText(logFilePath, $"[{DateTime.Now}] - Client connected: {socket.RemoteEndPoint}\n"); // Log kết nối client
                Console.WriteLine($"\nNew connection from {socket.RemoteEndPoint}");
            }
        }
        catch (Exception ex)
        {
            IPAddress address = IPAddress.Parse(IP_ADDRESS);
            File.AppendAllText(logFilePath, $"[{DateTime.Now}] - Server started on {address}:{PORT_NUMBER} failed with Error:\n {ex.Message}\nn--------------------------------------------------------n");
            Console.WriteLine("Error: " + ex);
        }
    }

    // Xử lý kết nối từ client
    // Các hàm RESTful API trong đây
    private static void HandleClient(Socket socket)
    {
        try
        {
            byte[] buffer = new byte[BUFFER_SIZE];                      // bla bla bla
            int received = socket.Receive(buffer);                      // Nhận dữ liệu từ client   
            string request = encoding.GetString(buffer, 0, received);   // Chuyển dữ liệu thành chuỗi

            // Xử lý WebSocket handshake
            if (request.Contains("Upgrade: websocket"))
            {
                string roomName = HandleWebSocketHandshake(socket, request);
                HandleWebSocketCommunication(socket, roomName);
                return;
            }

            string[] lines = request.Split("\r\n");
            string[] requestLine = lines[0].Split(' ');

            // Xử lí Login
            if (requestLine[0] == "POST" && requestLine[1] == "/login")
            {
                // Đọc nội dung POST
                int contentLength = 0;
                foreach (string line in lines)
                {
                    if (line.StartsWith("Content-Length:"))
                    {
                        contentLength = int.Parse(line.Substring("Content-Length:".Length).Trim());
                    }
                }

                string body = "";
                if (contentLength > 0)
                {
                    byte[] bodyBuffer = new byte[contentLength];
                    int totalRead = 0;
                    while (totalRead < contentLength)
                    {
                        totalRead += socket.Receive(bodyBuffer, totalRead, contentLength - totalRead, SocketFlags.None);
                    }
                    body = encoding.GetString(bodyBuffer, 0, contentLength);
                }

                // Phân tích dữ liệu từ body
                Dictionary<string, string> formData = new();
                foreach (string pair in body.Split('&'))
                {
                    string[] kv = pair.Split('=');
                    if (kv.Length == 2)
                    {
                        formData[WebUtility.UrlDecode(kv[0])] = WebUtility.UrlDecode(kv[1]);
                    }
                }

                string username = formData.ContainsKey("username") ? formData["username"] : "";
                string password = formData.ContainsKey("password") ? formData["password"] : "";

                // Kiểm tra từ file JSON (data/user.json)
                User? foundUser = users.Find(u => u.username == username && u.password == password);
                if (foundUser != null)
                {
                    Console.WriteLine($"User '{foundUser.username}' logged in as {foundUser.role}");

                    // 🌟 SESSION TOKEN
                    string sessionToken = Guid.NewGuid().ToString();
                    sessions[sessionToken] = foundUser.username;

                    // Trả về cookie và chuyển hướng
                    string login_response = "HTTP/1.1 302 Found\r\n";
                    login_response += "Location: /join\r\n";
                    login_response += $"Set-Cookie: session={sessionToken}; Path=/; HttpOnly\r\n";
                    login_response += "\r\n";

                    socket.Send(encoding.GetBytes(login_response));
                }
                else
                {
                    string errorResponse = "HTTP/1.1 401 Unauthorized\r\nContent-Type: text/plain\r\n\r\nInvalid username or password";
                    socket.Send(encoding.GetBytes(errorResponse));
                }

                socket.Close();
                return;
            }

            // Xử lí logout
            else if (requestLine[0] == "GET" && requestLine[1] == "/logout")
            {
                // Lấy session ID và COOKIE
                string? sessionToken = null;
                foreach (string line in lines)
                {
                    if (line.StartsWith("Cookie:"))
                    {
                        string[] cookies = line.Substring("Cookie:".Length).Trim().Split(';');
                        foreach (string cookie in cookies)
                        {
                            string[] kv = cookie.Trim().Split('=');
                            if (kv.Length == 2 && kv[0] == "session")
                            {
                                sessionToken = kv[1];
                            }
                        }
                    }
                }
                string username = "Unknown";
                if (sessionToken != null && sessions.TryGetValue(sessionToken, out var foundUsername))
                {
                    username = foundUsername;
                }

                // Xóa session khỏi bộ nhớ
                if (sessionToken != null && sessions.ContainsKey(sessionToken))
                {
                    sessions.Remove(sessionToken);
                }
                // xóa cookie
                string logout_response = "HTTP/1.1 302 Found\r\n";
                logout_response += "Location: /login\r\n";
                logout_response += "Set-Cookie: session=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT\r\n";
                logout_response += "\r\n";

                Console.WriteLine($"User {username} logged out successfully.");
                socket.Send(encoding.GetBytes(logout_response));
                socket.Close();
                return;
            }

            // Xử lí Login as Guest
            else if (requestLine[0] == "GET" && requestLine[1] == "/guest")
            {
                string guestName = "Guest" + rand.Next(1000, 9999);

                // SESSION TOKEN dành cho guest
                string sessionToken = Guid.NewGuid().ToString();
                sessions[sessionToken] = guestName;

                Console.WriteLine($"User '{guestName}' logged in as Guest");

                // Redirect to /join
                string guestResponse = "HTTP/1.1 302 Found\r\n";
                guestResponse += "Location: /join\r\n";
                guestResponse += $"Set-Cookie: session={sessionToken}; Path=/; HttpOnly\r\n";
                guestResponse += "\r\n";

                socket.Send(encoding.GetBytes(guestResponse));
                socket.Close();
                return;
            }

            // Xử lí yêu cầu POST tạo phòng mới 
            else if (requestLine[0] == "POST" && requestLine[1] == "/createroom")
            {
                // Đọc nội dung POST
                int contentLength = 0;
                foreach (string line in lines)
                {
                    if (line.StartsWith("Content-Length:"))
                    {
                        contentLength = int.Parse(line.Substring("Content-Length:".Length).Trim());
                    }
                }

                string requestBody = "";
                if (contentLength > 0)
                {
                    byte[] bodyBuffer = new byte[contentLength];
                    int totalRead = 0;
                    while (totalRead < contentLength)
                    {
                        totalRead += socket.Receive(bodyBuffer, totalRead, contentLength - totalRead, SocketFlags.None);
                    }
                    requestBody = encoding.GetString(bodyBuffer, 0, contentLength);
                }

                var roomData = System.Web.HttpUtility.ParseQueryString(requestBody);
                string? roomName = roomData["roomname"];
                string? sessionToken = null;

                // Parse headers from request
                Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines)
                {
                    int sep = line.IndexOf(':');
                    if (sep > 0)
                    {
                        string key = line.Substring(0, sep).Trim();
                        string value = line.Substring(sep + 1).Trim();
                        headers[key] = value;
                    }
                }

                // Console.WriteLine($"Creating room: {roomName}");
                if (headers.ContainsKey("Cookie"))
                {
                    string[] cookies = headers["Cookie"].Split(';');
                    foreach (string cookie in cookies)
                    {
                        var parts = cookie.Split('=');
                        if (parts.Length == 2 && parts[0].Trim() == "session")
                        {
                            sessionToken = parts[1].Trim();
                            break;
                        }
                    }
                }

                string? username = null;
                if (sessionToken != null && sessions.ContainsKey(sessionToken))
                {
                    username = sessions[sessionToken];
                }

                string roomFilePath = $"data/rooms/room_{roomName}.txt";

                if (File.Exists(roomFilePath))
                {
                    string alreadyExists = "HTTP/1.1 409 Conflict\r\nContent-Type: text/plain\r\n\r\nRoom already exists.";
                    socket.Send(encoding.GetBytes(alreadyExists));
                }
                else
                {
                    Directory.CreateDirectory("data/rooms");
                    using (var writer = new StreamWriter(roomFilePath))
                    {
                        writer.WriteLine($"Room: {roomName}");
                        writer.WriteLine($"Created at: {DateTime.Now}");
                        writer.WriteLine($"Created by: {username ?? "Unknown"}");
                        writer.WriteLine("\n----- Chat Log -----");
                    }

                    string created = "HTTP/1.1 201 Created\r\nContent-Type: text/plain\r\n\r\nRoom created successfully.";
                    socket.Send(encoding.GetBytes(created));
                }

                socket.Close();
                return;
            }

            else if (requestLine[0] == "GET" && requestLine[1] == "/whoami")
            {
                string? sessionToken = null;
                foreach (string line in lines)
                {
                    if (line.StartsWith("Cookie:"))
                    {
                        string[] cookies = line.Substring("Cookie:".Length).Trim().Split(';');
                        foreach (string cookie in cookies)
                        {
                            string[] kv = cookie.Trim().Split('=');
                            if (kv.Length == 2 && kv[0] == "session")
                            {
                                sessionToken = kv[1];
                            }
                        }
                    }
                }

                string username = "Guest";
                if (sessionToken != null && sessions.TryGetValue(sessionToken, out var foundUsername))
                {
                    username = foundUsername;
                }

                string json = System.Text.Json.JsonSerializer.Serialize(new { username });
                string whoamiResponse = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n\r\n{json}";
                socket.Send(encoding.GetBytes(whoamiResponse));
                socket.Close();
                return;
            }

            // Xử lí yêu cầu GET room log
            else if (requestLine[0] == "GET" && requestLine[1].StartsWith("/roomlog"))
            {
                string[] split = requestLine[1].Split('?');
                string roomName = "default";

                if (split.Length > 1)
                {
                    var query = System.Web.HttpUtility.ParseQueryString(new Uri("http:" + IP_ADDRESS + ":9999" + requestLine[1]).Query);
                    roomName = query["room"] ?? "default";
                }
                Console.WriteLine($"Fetching log for room: {roomName}");
                string logPath = $"data/rooms/room_{roomName}.txt";
                if (File.Exists(logPath))
                {
                    string log = File.ReadAllText(logPath);
                    string roomLogResponse = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\n{log}";
                    socket.Send(encoding.GetBytes(roomLogResponse));
                }
                else
                {
                    string roomLogResponse = "HTTP/1.1 404 Not Found\r\nContent-Type: text/plain\r\n\r\nRoom log not found";
                    socket.Send(encoding.GetBytes(roomLogResponse));
                }

                socket.Close();
                return;
            }

            // Xử lí yêu cầu GET danh sách room
            else if (requestLine[0] == "GET" && requestLine[1] == "/rooms")
            {
                string roomsDirectory = "data/rooms";
                List<string> roomNames = new List<string>();

                if (Directory.Exists(roomsDirectory))
                {
                    var files = Directory.GetFiles(roomsDirectory, "room_*.txt");
                    foreach (var file in files)
                    {
                        string[] roomFiles = Directory.GetFiles("data/rooms", "room_*.txt");
                        roomNames = roomFiles
                            .Select(file => Path.GetFileNameWithoutExtension(file)) // e.g., "room_TEST"
                            .Select(name => name.Replace("room_", ""))              // --> "TEST"
                            .ToList();
                    }
                }

                string json = System.Text.Json.JsonSerializer.Serialize(roomNames);
                string roomListResponse = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n\r\n{json}";
                socket.Send(encoding.GetBytes(roomListResponse));
                socket.Close();
                return;
            }

            // xử lí ROUTING
            if (requestLine.Length < 2)
            {
                socket.Close();
                return;
            }

            // Các routing
            string path = requestLine[1];
            string? fileName = path switch
            {
                "/" => "pages/homepage.html",            // Các file HTML   
                "/chat" => "pages/chat.html",
                "/login" => "pages/login.html",
                "/join" => "pages/joinChat.html",
                "/homepage.css" => "pages/homepage.css", // Các file CSS
                "/chat.css" => "pages/chat.css",
                "/login.css" => "pages/login.css",
                "/joinChat.css" => "pages/joinChat.css",
                _ => null
            };
            string response;

            if (fileName != null && File.Exists(fileName))
            {
                string extension = Path.GetExtension(fileName);
                string contentType = extension switch // Dựa vào phần mở rộng của FILE để xác định Content_Type
                {
                    ".html" => "text/html",
                    ".css" => "text/css",
                    _ => "text/plain"
                };

                string fileContent = File.ReadAllText(fileName);
                response = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\n\r\n{fileContent}";
            }
            else
            {
                response = "HTTP/1.1 404 Not Found\r\nContent-Type: text/plain\r\n\r\nPage not found";
            }

            socket.Send(encoding.GetBytes(response));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Client error: " + ex.Message);
        }
        finally
        {
            if (socket.Connected) socket.Close();
        }
    }

    // Xử lý WebSocket handshake
    private static string HandleWebSocketHandshake(Socket socket, string request)
    {
        string? roomName = "default";
        string[] lines = request.Split("\r\n");
        string firstLine = lines[0];
        if (firstLine.StartsWith("GET "))
        {
            var uri = firstLine.Split(' ')[1];
            var query = System.Web.HttpUtility.ParseQueryString(new Uri("http://" + IP_ADDRESS + ":9999" + uri).Query);
            roomName = query["room"] ?? "default";
        }

        // Lấy Sec-WebSocket-Key từ request
        string? secWebSocketKey = GetWebSocketKey(request);
        if (secWebSocketKey == null)
        {
            throw new Exception("Missing Sec-WebSocket-Key");
        }

        // Tạo Sec-WebSocket-Accept
        string secWebSocketAccept;
        using (var sha1 = SHA1.Create())
        {
            string concat = secWebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(concat));
            secWebSocketAccept = Convert.ToBase64String(hash);
        }

        // Gửi phản hồi WebSocket handshake
        string response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: " + secWebSocketAccept + "\r\n\r\n";

        socket.Send(Encoding.UTF8.GetBytes(response));

        // Thêm client vào phòng chat
        lock (clientsLock)
        {
            if (!chatRooms.ContainsKey(roomName)) chatRooms[roomName] = new Room { Name = roomName };
            chatRooms[roomName].Clients.Add(socket);
        }
        Console.WriteLine("Handling WebSocket handshake for room: " + roomName);
        string logPath = $"data/rooms/room_{roomName}.txt";
        Directory.CreateDirectory("data/rooms");
        File.AppendAllText(logPath, $"[JOIN] {DateTime.Now} - {socket.RemoteEndPoint} joined {roomName}\n");
        Console.WriteLine("WebSocket handshake completed with " + socket.RemoteEndPoint);

        return roomName;
    }

    // Xử lý giao tiếp WebSocket
    // Nhận dữ liệu từ client, giải mã, xử lý và gửi lại dữ liệu cho tất cả các client trong phòng chat
    private static void HandleWebSocketCommunication(Socket socket, string roomName)
    {
        Console.WriteLine("Handling WebSocket communication for room: " + roomName);
        byte[] buffer = new byte[BUFFER_SIZE];
        string ipAddress = socket.RemoteEndPoint is IPEndPoint remoteEndPoint && remoteEndPoint != null
            ? remoteEndPoint.Address.ToString()
            : "Unknown";

        try
        {
            while (true)
            {
                int received = socket.Receive(buffer);
                if (received == 0) break;

                string message = DecodeWebSocketMessage(buffer, received);

                string nickname = "[Anonymous]";
                string color = "#000";
                string text = message;

                // Try parse JSON
                try
                {
                    var json = System.Text.Json.JsonDocument.Parse(message).RootElement;
                    if (json.TryGetProperty("nickname", out var n))
                    {
                        var nickValue = n.GetString();
                        if (!string.IsNullOrEmpty(nickValue))
                            nickname = nickValue;
                    }
                    if (json.TryGetProperty("color", out var c))
                    {
                        var colorValue = c.GetString();
                        if (!string.IsNullOrEmpty(colorValue))
                            color = colorValue;
                    }
                    if (json.TryGetProperty("msg", out var m))
                    {
                        var textValue = m.GetString();
                        if (!string.IsNullOrEmpty(textValue))
                            text = textValue;
                    }
                }
                catch
                {
                    Console.WriteLine("BOI THEY LEFT!?");
                    break;
                }

                string formatted = $"<span style='color:{color}'><b>{nickname}:</b></span> {System.Net.WebUtility.HtmlEncode(text)}";
                string fullMessage = $"[{ipAddress}] {formatted}";
                Console.WriteLine("Received: " + fullMessage);

                BroadcastWebSocketMessage(formatted, socket, roomName, nickname);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("WebSocket closed: " + ex.Message);
        }
        finally
        {
            lock (clientsLock)
            {
                if (chatRooms.ContainsKey(roomName))
                {
                    chatRooms[roomName].Clients.Remove(socket);
                }
            }
            File.AppendAllText($"data/rooms/room_{roomName}.txt", $"[LEAVE] {DateTime.Now} - {socket.RemoteEndPoint} left {roomName}\n");

            socket.Close();
        }
    }

    // Gửi tin nhắn đến tất cả các client trong phòng chat
    // Mã hóa tin nhắn thành frame WebSocket và gửi đến tất cả các client trong phòng chat
    private static void BroadcastWebSocketMessage(string message, Socket sender, string roomName, string userName)
    {
        byte[] frame = EncodeWebSocketMessage(message);
        Console.WriteLine($"Broadcasting message to room '{roomName}': {message}");
        string logPath = $"data/rooms/room_{roomName}.txt";

        lock (clientsLock)
        {
            if (!chatRooms.ContainsKey(roomName)) return;
            foreach (Socket client in chatRooms[roomName].Clients)
            {
                if (client.Connected)
                {
                    try
                    {
                        client.Send(frame);
                    }
                    catch
                    {
                        Console.WriteLine("WHAT GOING HEREEEEe?");
                    }
                }
            }
        }
        File.AppendAllText(logPath, $"[MSG] - {userName} - {DateTime.Now} - {message}\n");
    }

    private static string? GetWebSocketKey(string request)
    {
        foreach (string line in request.Split("\r\n"))
        {
            if (line.StartsWith("Sec-WebSocket-Key:"))
            {
                return line.Substring("Sec-WebSocket-Key:".Length).Trim();
            }
        }
        return null;
    }

    // Giải mã tin nhắn WebSocket
    private static string DecodeWebSocketMessage(byte[] buffer, int length)
    {
        int secondByte = buffer[1];
        int dataLength = secondByte & 127;
        int indexFirstMask = 2;

        if (dataLength == 126) indexFirstMask = 4;
        else if (dataLength == 127) indexFirstMask = 10;

        byte[] masks = new byte[4] {
            buffer[indexFirstMask], buffer[indexFirstMask + 1],
            buffer[indexFirstMask + 2], buffer[indexFirstMask + 3]
        };

        int indexFirstDataByte = indexFirstMask + 4;
        int messageLength = length - indexFirstDataByte;

        byte[] decoded = new byte[messageLength];
        for (int i = 0; i < messageLength; i++)
        {
            decoded[i] = (byte)(buffer[indexFirstDataByte + i] ^ masks[i % 4]);
        }

        return Encoding.UTF8.GetString(decoded);
    }

    // Mã hóa tin nhắn WebSocket
    private static byte[] EncodeWebSocketMessage(string message)
    {
        byte[] bytesRaw = Encoding.UTF8.GetBytes(message);
        byte[] frame = new byte[10 + bytesRaw.Length];
        int index = 0;

        frame[index++] = 0x81; // FIN + Text frame

        if (bytesRaw.Length <= 125)
        {
            frame[index++] = (byte)bytesRaw.Length;
        }
        else if (bytesRaw.Length >= 126 && bytesRaw.Length <= 65535)
        {
            frame[index++] = 126;
            frame[index++] = (byte)((bytesRaw.Length >> 8) & 255);
            frame[index++] = (byte)(bytesRaw.Length & 255);
        }
        else
        {
            frame[index++] = 127;
            for (int i = 7; i >= 0; i--)
            {
                frame[index++] = (byte)((bytesRaw.Length >> (8 * i)) & 255);
            }
        }

        Array.Copy(bytesRaw, 0, frame, index, bytesRaw.Length);
        byte[] finalFrame = new byte[index + bytesRaw.Length];
        Array.Copy(frame, finalFrame, finalFrame.Length);
        return finalFrame;
    }
}

// Lớp Room, các phòng chat thứ N
class Room
{
    public string? Name;
    public List<Socket> Clients = new List<Socket>();
}

// Lớp User, các người dùng
public class User
{
    public string username { get; set; } = ""; // Đang dùng
    public string password { get; set; } = ""; // Đang dùng
    public string role { get; set; } = ""; // Đang không dùng
}
