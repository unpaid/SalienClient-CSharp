# SalienClient-CSharp

Two C# classes for cheating Steam's Summer Sailen Game.  No browser / GUI needed.

### Dependencies
```
Newtonsoft.Json
SteamKit2 (if you do not replace the EResult type in 'GetTokenResponse')
```

### How To Use

If you do not have a web client to do authenticated requests on steamcommunity, use `SalienClient-HttpClient`. If you already have your `Token`, you can also use `SalienClient-HttpClient` if you wish.

If you need to get your `Token`, go to https://steamcommunity.com/saliengame/gettoken while logged in.

#### SalienClient-HttpClient
```
SalienClient Client = new SalienClient("TOKEN");
Client.Start();
```

#### SalienClient-SteamWeb
```
SalienClient Client = new SalienClient(ref SteamWebClient);
Client.Start();
```

### Screenshot

![Screenshot](https://i.imgur.com/qaWueUc.png)

### Credits
[Python Steam-Salien-Cheat by Nathan78906](https://github.com/nathan78906/steam-salien-cheat)
