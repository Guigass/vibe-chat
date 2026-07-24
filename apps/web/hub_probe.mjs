import * as signalR from '@microsoft/signalr';
const connection = new signalR.HubConnectionBuilder()
  .withUrl(process.env.HUB_URL)
  .configureLogging(signalR.LogLevel.Warning)
  .build();
connection.on('MessageCreated', (payload) => {
  console.log('EVENT MessageCreated', typeof payload, JSON.stringify(payload).slice(0, 600));
});
await connection.start();
await connection.invoke('JoinChannel', process.env.TENANT, process.env.CHANNEL);
console.log('joined');
await new Promise((r) => setTimeout(r, 12000));
await connection.stop();
