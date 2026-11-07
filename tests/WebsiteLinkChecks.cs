namespace B0r3dQR.Tests
{
    // GENERATED TEST DATA. What qr.b0r3d.org's own link checks (src/utils/linkChecks.ts in the website repo) find in
    // these links, by a script outside this repo (qr-pc-plan/fixtures/gen-website-link-checks.mjs): the tracking
    // parameters, and the link without them.
    static partial class TestMain
    {
        static readonly (string Text, string[] Tracking, string Clean)[] WebsiteTrackingChecks =
        {
            ("https://b0r3d.org/p?a=1&utm_medium=qr&b=%20x&gclid=9#frag", new[] { "utm_medium", "gclid" }, "https://b0r3d.org/p?a=1&b=%20x#frag"),
            ("https://b0r3d.org/", new string[0], "https://b0r3d.org/"),
            ("https://b0r3d.org/?a=1", new string[0], "https://b0r3d.org/?a=1"),
            ("https://youtu.be/x?si=abc", new[] { "si" }, "https://youtu.be/x"),
            ("https://b0r3d.org/?si=1", new string[0], "https://b0r3d.org/?si=1"),
            ("https://music.youtube.com/watch?v=1&si=2", new[] { "si" }, "https://music.youtube.com/watch?v=1"),
            ("https://YOUTUBE.COM/x?si=1", new[] { "si" }, "https://YOUTUBE.COM/x"),
            ("https://a@youtube.com:8080/?si=1", new[] { "si" }, "https://a@youtube.com:8080/"),
            ("https://open.spotify.com/track/1?si=x&utm_source=copy", new[] { "si", "utm_source" }, "https://open.spotify.com/track/1"),
            ("https://b0r3d.org/?UTM_SOURCE=x&Fbclid=1&MSCLKID=2", new[] { "UTM_SOURCE", "Fbclid", "MSCLKID" }, "https://b0r3d.org/"),
            ("HTTPS://b0r3d.org/?gclid=1", new[] { "gclid" }, "HTTPS://b0r3d.org/"),
            ("https://b0r3d.org?gclid=1", new[] { "gclid" }, "https://b0r3d.org"),
            ("https://b0r3d.org/?gclid", new[] { "gclid" }, "https://b0r3d.org/"),
            ("https://b0r3d.org/?=1&gclid=1", new[] { "gclid" }, "https://b0r3d.org/?=1"),
            ("https://b0r3d.org/?a=1&&gclid=1", new[] { "gclid" }, "https://b0r3d.org/?a=1"),
            ("https://b0r3d.org/?gclid=1&gclid=2", new[] { "gclid" }, "https://b0r3d.org/"),
            ("https://b0r3d.org/?%55TM_x=1", new[] { "UTM_x" }, "https://b0r3d.org/"),
            ("https://b0r3d.org/?utm%5Fx=1", new[] { "utm_x" }, "https://b0r3d.org/"),
            ("https://b0r3d.org/?utm_x+y=1", new[] { "utm_x y" }, "https://b0r3d.org/"),
            ("https://b0r3d.org/?%ZZ=1&gclid=2", new[] { "gclid" }, "https://b0r3d.org/?%ZZ=1"),
            ("https://[::1]:80/?si=1&gclid=1", new[] { "gclid" }, "https://[::1]:80/?si=1"),
            ("https://b0r3d.org/?m\u212at_tok=1&a=2", new[] { "m\u212at_tok" }, "https://b0r3d.org/?a=2"),
            ("https://b0r3d.org/?MSCL\u212aID=1", new[] { "MSCL\u212aID" }, "https://b0r3d.org/"),
            ("https://b0r3d.org/?FBCL\u0130D=1", new string[0], "https://b0r3d.org/?FBCL\u0130D=1"),
            ("https://b0r3d.org/?fbcl\u0131d=1", new string[0], "https://b0r3d.org/?fbcl\u0131d=1"),
            ("https://youtu.be/x?\u017fi=1", new string[0], "https://youtu.be/x?\u017fi=1"),
            ("https://b0r3d.org/?\u0391\u03a3=1&utm_\u03a3=2", new[] { "utm_\u03a3" }, "https://b0r3d.org/?\u0391\u03a3=1"),
            ("https://spot\u0130fy.com/?si=1", new string[0], "https://spot\u0130fy.com/?si=1"),
        };
    }
}
