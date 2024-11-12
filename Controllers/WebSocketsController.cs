using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace WebSockets_TestAPI.Controllers
{
    public class WebSocketsController
    {
        // Delegado para obtener las peticiones realizadas
        private readonly RequestDelegate _next;
        public WebSocketsController(RequestDelegate next) { _next = next; }

        // Método Invoke - Capturar las peticiones
        public async Task Invoke(HttpContext context)
        {
            // Si no es una petición socket, no procesar controller
            if (!context.WebSockets.IsWebSocketRequest)
            {
                await _next.Invoke(context);
                return;
            }

            // Es una petición Socket, procesar controller (decodificar mensaje)
            var ct = context.RequestAborted;
            using (var socket = await context.WebSockets.AcceptWebSocketAsync())
            {
                // Iniciar una tarea de envío automático
                var sendMessagesTask = SendPeriodicMessagesAsync(socket, ct);

                // Procesar mensajes del Cliente
                while(socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var mensaje = await ReceiveStringAsync(socket, ct);
                    if (mensaje == null) break;

                    // Vamos a procesar dos tipos de mensajes
                    // 1. Mensajes Simples -> sólo llega una cadena de texto
                    // 2. Mensajes Compuestos -> se requieren parámetros. Separamos el mensaje de los parámetros con #

                    //Procesar Mensajes Compuestos
                    if (mensaje.Contains('#'))
                    {
                        string[] mensajeCompuesto = mensaje.ToLower().Split('#');
                        switch (mensajeCompuesto[0])
                        {
                            case "hola":
                                await SendStringAsync(socket, "Hola Usuario" + mensajeCompuesto[1], ct);
                                break;
                            default:
                                await SendStringAsync(socket, "No se entiende el mensaje.", ct);
                                break;
                        }
                    }
                    else
                    {
                        // Procesar Mensajes Simples
                        switch (mensaje.ToLower())
                        {
                            case "hola":
                                await SendStringAsync(socket, "Hola, Bienvenido...", ct);
                                break;
                            case "adios":
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Desconectado", ct);
                                break;
                            default:
                                await SendStringAsync(socket, "No se entiende el mensaje", ct);
                                break;
                        }
                    }
                }

                await sendMessagesTask;

            }
        }

        private async Task SendPeriodicMessagesAsync(WebSocket socket, CancellationToken ct)
        {
            var elapsed = 0;
            var interval = 2; // Intervalo en segundos para enviar mensaje "Sigo escuchando..."
            var timeout = 60; // Tiempo total en segundos para cerrar la conexión
            var toggleMessage = true;

            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // Crear un objeto JSON para el mensaje "Sigo escuchando..."
                var messageObject = new
                {
                    type = "payment_status",  // Cambiar el tipo a "payment_status"
                    message = toggleMessage ? "Pagado" : "Denegado",
                    result = toggleMessage ? 1 : 2
                };

                // Convertir el objeto a JSON y enviarlo
                var messageJson = JsonSerializer.Serialize(messageObject);
                await SendStringAsync(socket, messageJson, ct);
                toggleMessage = !toggleMessage;

                await Task.Delay(TimeSpan.FromSeconds(interval), ct);

                // Incrementar el tiempo transcurrido
                elapsed += interval;

                // Si el tiempo transcurrido supera el timeout, cerrar la conexión
                if (elapsed >= timeout)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Timeout. Se cerró la conexión.", ct);
                    break;
                }
            }
        }


        private static async Task<string> ReceiveStringAsync(WebSocket socket, CancellationToken ct = default)
        {
            // Decodificar Mensaje Recibido
            var buffer = new ArraySegment<byte>(new byte[8192]);
            using (var message = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    ct.ThrowIfCancellationRequested();

                    result = await socket.ReceiveAsync(buffer, ct);
                    message.Write(buffer.Array, buffer.Offset, result.Count);
                }
                while (!result.EndOfMessage);

                message.Seek(0, SeekOrigin.Begin);
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new Exception("Mensaje Inesperado");

                // Codificar como UTF8: http://tools.ietf.org/html/rfc6455#section-5.6
                using (var reader = new StreamReader(message, Encoding.UTF8)) 
                { 
                    return await reader.ReadToEndAsync(); 
                }
            }

        }

        private static Task SendStringAsync(WebSocket socket, string data, CancellationToken ct = default)
        {
            var buffer = Encoding.UTF8.GetBytes(data);
            var segment = new ArraySegment<byte>(buffer);
            return socket.SendAsync(segment, WebSocketMessageType.Text, true, ct);
        }
    }
}
