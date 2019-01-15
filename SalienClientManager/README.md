# SalienClient-CSharp

Two C# classes for cheating Steam's Summer Sailen Game.  No browser / GUI needed.  Designed for multiple accounts.

### Dependencies
```
Newtonsoft.Json
```

### How To Use

Create a file called `tokens.txt` in the same folder as the executable, the syntax of this file is as follows

```
UsernameOne:TokenOne
UsernameTwo:TokenTwo
```

The `Username` doesn't have to be the username of the account, it's just to prevent clutter in the console

If you do not have a `Token`, go to https://steamcommunity.com/saliengame/gettoken while logged in to get it

If you already have a bot of some sort, you can add `SalienClient.cs` as a reference to your project and retrieve the `Token` using an authenticated Steam web client like so:

```
SalienClient.GetTokenResponse TokenResponse = JsonConvert.DeserializeObject<SalienClient.GetTokenResponse>(SteamWebClient.Request(
	"https://steamcommunity.com/saliengame/gettoken", // URL
	"GET",                                            // Method
	null,                                             // Data
	"https://steamcommunity.com/saliengame/play/"     // Referer
));

if (TokenResponse.Success == EResult.OK)
{
	SalienClient Client = new SalienClient(Username, TokenResponse.Token);
	Client.Start();
}
```

### Screenshot

![Screenshot](https://i.imgur.com/RsxTTjN.png)

### Credits
[Python Steam-Salien-Cheat by Nathan78906](https://github.com/nathan78906/steam-salien-cheat)\
[Python SalienCheat by xPaw](https://github.com/SteamDatabase/SalienCheat)