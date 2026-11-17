using System.Net;

namespace Bank.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ProfilePhotoTests(BankApiFactory factory)
{
    private static Task<HttpResponseMessage> PutPhotoAsync(HttpClient client, byte[] bytes, string fileName = "foto.png") =>
        client.PutAsync("/api/account/photo", new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", fileName } });

    [Fact]
    public async Task Photo_is_saved_served_back_and_removed()
    {
        var user = await TestUsers.CreateAsync(factory);
        Assert.False((await user.Client.AccountAsync()).HasPhoto);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync("/api/account/photo")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await PutPhotoAsync(user.Client, TestUsers.Png)).StatusCode);
        Assert.True((await user.Client.AccountAsync()).HasPhoto);

        var photo = await user.Client.GetAsync("/api/account/photo");
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/png", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestUsers.Png, await photo.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync("/api/account/photo")).StatusCode);
        Assert.False((await user.Client.AccountAsync()).HasPhoto);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync("/api/account/photo")).StatusCode);
    }

    [Fact]
    public async Task Photo_must_really_be_an_image()
    {
        var user = await TestUsers.CreateAsync(factory);
        // a PDF renamed to .png is rejected by its content
        var res = await PutPhotoAsync(user.Client, "%PDF-1.7 fake"u8.ToArray());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Contains("photo.type", await res.Content.ReadAsStringAsync());
        Assert.False((await user.Client.AccountAsync()).HasPhoto);
    }

    [Fact]
    public async Task Each_user_only_sees_their_own_photo()
    {
        var owner = await TestUsers.CreateAsync(factory);
        var other = await TestUsers.CreateAsync(factory);
        (await PutPhotoAsync(owner.Client, TestUsers.Png)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync("/api/account/photo")).StatusCode);
    }
}
