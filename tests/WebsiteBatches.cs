namespace B0r3dQR.Tests
{
    // GENERATED TEST DATA. What qr.b0r3d.org's own batch code (src/utils/csv.ts and csvBatchProcessing.ts in the
    // website repo) makes of these CSV files, by a script outside this repo (gen-website-batches.mjs):
    // for a QR batch, whether the file is taken and each row's data, frame text, file name, font, whether a frame
    // goes on, and the file names in the batch; for a barcode batch, each row's number, data and file name, and the
    // file names; and the text the website reads from a file's bytes.
    static partial class TestMain
    {
        static readonly (string Name, string Csv, bool Valid, string[] Data, string[] FrameTexts, string[] FileNames, string[] Fonts, bool HasFrameText, string[] ExportNames)[] WebsiteQrBatches =
        {
            ("links", "url\nhttps://b0r3d.org\nhttps://b0r3d.org/qr\nhttps://example.com/a/b/c", true, new[] { "https://b0r3d.org", "https://b0r3d.org/qr", "https://example.com/a/b/c" }, new[] { "", "", "" }, new[] { "", "", "" }, new[] { "", "", "" }, false, new[] { "b0r3d_org", "qr", "c" }),
            ("links with frames and names", "url,frameText,fileName\nhttps://b0r3d.org,Scan me,home\nhttps://b0r3d.org/x,Hello world,\nhttps://b0r3d.org/y,,named", true, new[] { "https://b0r3d.org", "https://b0r3d.org/x", "https://b0r3d.org/y" }, new[] { "Scan me", "Hello world", "" }, new[] { "home", "", "named" }, new[] { "", "", "" }, true, new[] { "home", "Hello_world", "named" }),
            ("fonts", "url,frameText,fileName,frameFontFamily\nhttps://b0r3d.org,Hi,a,Poppins\nhttps://b0r3d.org/2,There,b,\nhttps://b0r3d.org/3,,c,\"'Courier New', monospace\"", true, new[] { "https://b0r3d.org", "https://b0r3d.org/2", "https://b0r3d.org/3" }, new[] { "Hi", "There", "" }, new[] { "a", "b", "c" }, new[] { "Poppins", "", "Courier New', monospace" }, true, new[] { "a", "b", "c" }),
            ("semicolons", "url;frameText;fileName\nhttps://b0r3d.org;Scan, please;one\ntext with, commas;;two", true, new[] { "https://b0r3d.org", "text with, commas" }, new[] { "Scan, please", "" }, new[] { "one", "two" }, new[] { "", "" }, true, new[] { "one", "two" }),
            ("tabs", "url\tframeText\nhttps://b0r3d.org\ttab framed\nplain text\t", true, new[] { "https://b0r3d.org", "plain text" }, new[] { "tab framed", "" }, new[] { "", "" }, new[] { "", "" }, true, new[] { "tab_framed", "plain_text" }),
            ("quotes", "url,frameText\n\"a \"\"quoted\"\" text\",x\n\"comma, inside\",y\n'single quoted',z", true, new[] { "a \"quoted\" text", "comma, inside", "single quoted" }, new[] { "x", "y", "z" }, new[] { "", "", "" }, new[] { "", "", "" }, true, new[] { "x", "y", "z" }),
            ("newlines in quotes", "url,frameText\n\"line one\nline two\",multi\nafter,row", true, new[] { "line one\nline two", "after" }, new[] { "multi", "row" }, new[] { "", "" }, new[] { "", "" }, true, new[] { "multi", "row" }),
            ("crlf", "url,frameText,fileName\r\nhttps://b0r3d.org,One,1\r\nhttps://b0r3d.org/2,Two,2\r\n", true, new[] { "https://b0r3d.org", "https://b0r3d.org/2" }, new[] { "One", "Two" }, new[] { "1", "2" }, new[] { "", "" }, true, new[] { "1", "2" }),
            ("blank lines", "url\n\nhttps://b0r3d.org\n   \nsecond\n", true, new[] { "https://b0r3d.org", "second" }, new[] { "", "" }, new[] { "", "" }, new[] { "", "" }, false, new[] { "b0r3d_org", "second" }),
            ("spaces", "url , frameText\n  padded data  ,  padded frame  \nx,y", true, new[] { "padded data", "x" }, new[] { "padded frame", "y" }, new[] { "", "" }, new[] { "", "" }, true, new[] { "padded_frame", "y" }),
            ("same names", "url,frameText,fileName\nhttps://b0r3d.org/a,,same\nhttps://b0r3d.org/b,,same\nhttps://b0r3d.org/c,,same", true, new[] { "https://b0r3d.org/a", "https://b0r3d.org/b", "https://b0r3d.org/c" }, new[] { "", "", "" }, new[] { "same", "same", "same" }, new[] { "", "", "" }, false, new[] { "same", "same-1", "same-2" }),
            ("names from data", "url\nhttps://b0r3d.org/path/to/page_1\nhttps://b0r3d.org/dir/page-2\nhttps://b0r3d.org/x-y/z.html\nhttps://sub.example.co.uk/a b/c d\nftp://a/b/c\nplain text here\na/b\nhttps://b0r3d.org\nhttps://b0r3d.org/\nWIFI:T:WPA;S:home;P:pw;;\nmailto:a@b.c\n\u00dcn\u00efc\u00f6d\u00e9 text\n\u65e5\u672c\u8a9e", true, new[] { "https://b0r3d.org/path/to/page_1", "https://b0r3d.org/dir/page-2", "https://b0r3d.org/x-y/z.html", "https://sub.example.co.uk/a b/c d", "ftp://a/b/c", "plain text here", "a/b", "https://b0r3d.org", "https://b0r3d.org/", "WIFI:T:WPA;S:home;P:pw;;", "mailto:a@b.c", "\u00dcn\u00efc\u00f6d\u00e9 text", "\u65e5\u672c\u8a9e" }, new[] { "", "", "", "", "", "", "", "", "", "", "", "", "" }, new[] { "", "", "", "", "", "", "", "", "", "", "", "", "" }, new[] { "", "", "", "", "", "", "", "", "", "", "", "", "" }, false, new[] { "page_1", "dir", "b0r3d_org", "sub_example_co_uk", "c", "plain_text_here", "a_b", "b0r3d_org-7", "b0r3d_org-8", "WIFI_T_WPA_S_home_P_pw__", "mailto_a_b_c", "_n_c_d__text", "___" }),
            ("frame text names", "url,frameText\nhttps://b0r3d.org/1,Hello World!\nhttps://b0r3d.org/2,Hello World!\nhttps://b0r3d.org/3,\u00dcn\u00efc\u00f6d\u00e9", true, new[] { "https://b0r3d.org/1", "https://b0r3d.org/2", "https://b0r3d.org/3" }, new[] { "Hello World!", "Hello World!", "\u00dcn\u00efc\u00f6d\u00e9" }, new[] { "", "", "" }, new[] { "", "", "" }, true, new[] { "Hello_World_", "Hello_World_-1", "_n_c_d_" }),
            ("contacts", "firstName,lastName,org,position,phoneWork,phonePrivate,phoneMobile,email,website,street,zipcode,city,state,country,version,frameText,fileName\nRukus,Cat,b0r3d,Chief napper,+1 555 0100,,+1 555 0199,rukus@example.com,https://b0r3d.org,1 Loaf Lane,12345,Breadville,CA,USA,,Call me,rukus\nAnn,Lee,,,,,,,,,,,,,4.0,,\nBo,Str\u00f6m,Caf\u00e9; Bar,\"Boss, big\",,,,,,,,,,,2.1,,bo", true, new[] { "BEGIN:VCARD\nVERSION:3.0\nN:Cat;Rukus;;;\nFN:Rukus Cat\nORG:b0r3d\nTITLE:Chief napper\nTEL;TYPE=WORK,VOICE:+1 555 0100\nTEL;TYPE=CELL,VOICE:+1 555 0199\nEMAIL:rukus@example.com\nURL:https://b0r3d.org\nADR;TYPE=WORK:;;1 Loaf Lane;Breadville;CA;12345;USA\nEND:VCARD", "BEGIN:VCARD\nVERSION:4.0\nN:Lee;Ann;;;\nFN:Ann Lee\nEND:VCARD", "BEGIN:VCARD\nVERSION:2.1\nN;CHARSET=UTF-8:Str\u00f6m;Bo;;;\nFN;CHARSET=UTF-8:Bo Str\u00f6m\nORG;CHARSET=UTF-8:Caf\u00e9\\; Bar\nTITLE:Boss\\, big\nEND:VCARD" }, new[] { "Call me", "", "" }, new[] { "rukus", "", "bo" }, new[] { "", "", "" }, true, new[] { "rukus", "Ann_Lee", "bo" }),
            ("contacts, semicolons", "FirstName;LastName;Email\nJo;Doe;jo@example.com\nAl;Bee;", true, new[] { "BEGIN:VCARD\nVERSION:3.0\nN:Doe;Jo;;;\nFN:Jo Doe\nEMAIL:jo@example.com\nEND:VCARD", "BEGIN:VCARD\nVERSION:3.0\nN:Bee;Al;;;\nFN:Al Bee\nEND:VCARD" }, new[] { "", "" }, new[] { "", "" }, new[] { "", "" }, false, new[] { "Jo_Doe", "Al_Bee" }),
            ("contacts missing a name", "firstName,lastName\nAnn,Lee\n,NoFirst", false, null, null, null, null, false, null),
            ("header only", "url,frameText", false, null, null, null, null, false, null),
            ("empty", "", false, null, null, null, null, false, null),
            ("a row without data", "url,frameText\nhttps://b0r3d.org,a\n,b", false, null, null, null, null, false, null),
            ("one column, no header row", "https://b0r3d.org/first\nhttps://b0r3d.org/second", true, new[] { "https://b0r3d.org/second" }, new[] { "" }, new[] { "" }, new[] { "" }, false, new[] { "second" }),
            ("names with no site", "url\nftp://a.b/c.d/e.f\na/b.c/d.e\n///\nx//y\nhttp://h.example/p/q?x=1", true, new[] { "ftp://a.b/c.d/e.f", "a/b.c/d.e", "///", "x//y", "http://h.example/p/q?x=1" }, new[] { "", "", "", "", "" }, new[] { "", "", "", "", "" }, new[] { "", "", "", "", "" }, false, new[] { "qr_code_0", "qr_code_1", "qr_code_2", "y", "p" }),
            ("vCard text in a row", "url\nBEGIN:VCARD\n\"BEGIN:VCARD\nFN:  Ann   Lee  \nEND:VCARD\"\n\"BEGIN:VCARDFN:x\"", true, new[] { "BEGIN:VCARD", "BEGIN:VCARD\nFN:  Ann   Lee  \nEND:VCARD", "BEGIN:VCARDFN:x" }, new[] { "", "", "" }, new[] { "", "", "" }, new[] { "", "", "" }, false, new[] { "", "Ann_Lee", "x" }),
            ("contacts with accents in 2.1", "firstName,lastName,version\nBo,Str\u00e9m,2.1\nAl,Bee,4", true, new[] { "BEGIN:VCARD\nVERSION:2.1\nN;CHARSET=UTF-8:Str\u00e9m;Bo;;;\nFN;CHARSET=UTF-8:Bo Str\u00e9m\nEND:VCARD", "BEGIN:VCARD\nVERSION:4.0\nN:Bee;Al;;;\nFN:Al Bee\nEND:VCARD" }, new[] { "", "" }, new[] { "", "" }, new[] { "", "" }, false, new[] { "", "Al_Bee" }),
            ("contacts with odd spaces", "firstName,lastName,frameText\nAnn,Lee,\nx\ty,z\u00a0w,", true, new[] { "BEGIN:VCARD\nVERSION:3.0\nN:Lee;Ann;;;\nFN:Ann Lee\nEND:VCARD", "BEGIN:VCARD\nVERSION:3.0\nN:z\u00a0w;x\ty;;;\nFN:x\ty z\u00a0w\nEND:VCARD" }, new[] { "", "" }, new[] { "", "" }, new[] { "", "" }, false, new[] { "Ann_Lee", "x_y_z_w" }),
            ("a name taken twice", "url,frameText,fileName\na,,same\nb,,same\nc,,same\nd,,same-1", true, new[] { "a", "b", "c", "d" }, new[] { "", "", "", "" }, new[] { "same", "same", "same", "same-1" }, new[] { "", "", "", "" }, false, new[] { "same", "same-1", "same-2", "same-1-3" }),
            ("a caption of a space", "url,frameText,fileName\nx,' ',\ny,,a", true, new[] { "x", "y" }, new[] { " ", "" }, new[] { "", "a" }, new[] { "", "" }, false, new[] { "_", "a" }),
            ("sites in odd places", "url\na/b/c/http:///q/https://h.k/r.s\nHTTPS://H.K/a.b/c.d", true, new[] { "a/b/c/http:///q/https://h.k/r.s", "HTTPS://H.K/a.b/c.d" }, new[] { "", "" }, new[] { "", "" }, new[] { "", "" }, false, new[] { "h_k", "qr_code_1" }),
            ("vCard names, odd ones", "url\n\"BEGIN:VCARD\nFN:Ann\u2028Lee\nEND:VCARD\"\n\"BEGIN:VCARD\nFN:   \nFN:x\"", true, new[] { "BEGIN:VCARD\nFN:Ann\u2028Lee\nEND:VCARD", "BEGIN:VCARD\nFN:   \nFN:x" }, new[] { "", "" }, new[] { "", "" }, new[] { "", "" }, false, new[] { "Ann_Lee", "" }),
            ("a row of tabs", "url\tframeText\n\t\t\nx\ty", true, new[] { "x" }, new[] { "y" }, new[] { "" }, new[] { "" }, true, new[] { "y" }),
            ("CRs in quotes", "url,frameText\n\"a\rb\",x\n\"c\"\r\"d\",y\rz", true, new[] { "ab", "c\"d" }, new[] { "x", "y\rz" }, new[] { "", "" }, new[] { "", "" }, true, new[] { "x", "y_z" }),
            ("contacts with a Kelvin sign in a header", "firstName,lastName,phoneWor\u212a\nAnn,Lee,555", true, new[] { "BEGIN:VCARD\nVERSION:3.0\nN:Lee;Ann;;;\nFN:Ann Lee\nTEL;TYPE=WORK,VOICE:555\nEND:VCARD" }, new[] { "" }, new[] { "" }, new[] { "" }, false, new[] { "Ann_Lee" }),
            ("contacts in capitals", "FIRSTNAME,LASTNAME,PHONEWORK,FRAMETEXT\nAnn,Lee,555,Hi", true, new[] { "BEGIN:VCARD\nVERSION:3.0\nN:Lee;Ann;;;\nFN:Ann Lee\nTEL;TYPE=WORK,VOICE:555\nEND:VCARD" }, new[] { "Hi" }, new[] { "" }, new[] { "" }, true, new[] { "Hi" }),
            ("a capital I with a dot in a header", "f\u0130rstName,lastName\nAnn,Lee", true, new[] { "Ann" }, new[] { "Lee" }, new[] { "" }, new[] { "" }, true, new[] { "Lee" }),
        };

        static readonly (string Name, string Csv, (int Row, string Data, string FileName)[] Items, string[] ExportNames)[] WebsiteBarcodeBatches =
        {
            ("header data and fileName", "data,fileName\n9506000134352,first\n4006381333931,second\n4006381333931,", new (int, string, string)[] { (2, "9506000134352", "first"), (3, "4006381333931", "second"), (4, "4006381333931", null) }, new[] { "first", "second", "4006381333931" }),
            ("header code", "Code;Name\nB0R3D-1;one\nB0R3D-2;two", new (int, string, string)[] { (2, "B0R3D-1", "one"), (3, "B0R3D-2", "two") }, new[] { "one", "two" }),
            ("header barcode only", "barcode\n123\n456", new (int, string, string)[] { (2, "123", null), (3, "456", null) }, new[] { "123", "456" }),
            ("header file first", "file,value\na,1\nb,2", new (int, string, string)[] { (2, "1", "a"), (3, "2", "b") }, new[] { "a", "b" }),
            ("header file name with a space", "text,file name\nx,y", new (int, string, string)[] { (2, "x", "y") }, new[] { "y" }),
            ("no header", "9506000134352,first\n4006381333931,second", new (int, string, string)[] { (1, "9506000134352", "first"), (2, "4006381333931", "second") }, new[] { "first", "second" }),
            ("one column", "12345\n67890", new (int, string, string)[] { (1, "12345", null), (2, "67890", null) }, new[] { "12345", "67890" }),
            ("a QR batch file", "url,frameText,fileName\nhttps://b0r3d.org,Scan,home\nhttps://b0r3d.org/2,,", new (int, string, string)[] { (2, "https://b0r3d.org", "home"), (3, "https://b0r3d.org/2", null) }, new[] { "home", "https___b0r3d_org_2" }),
            ("bom", "\ufeffdata,fileName\nABC,x", new (int, string, string)[] { (2, "ABC", "x") }, new[] { "x" }),
            ("blank data rows", "data,fileName\n,nothing\n   ,spaces\nreal,kept", new (int, string, string)[] { (4, "real", "kept") }, new[] { "kept" }),
            ("same names", "data,fileName\nA,same\nB,same\nC,same\nD,same_2", new (int, string, string)[] { (2, "A", "same"), (3, "B", "same"), (4, "C", "same"), (5, "D", "same_2") }, new[] { "same", "same_2", "same_3", "same_2_2" }),
            ("names from data", "data\nB0R3D 128\nB0R3D 128\n\u00e9-\u00fc\n(01)09506000134352(10)ABC\na/b\\c", new (int, string, string)[] { (2, "B0R3D 128", null), (3, "B0R3D 128", null), (4, "\u00e9-\u00fc", null), (5, "(01)09506000134352(10)ABC", null), (6, "a/b\\c", null) }, new[] { "B0R3D_128", "B0R3D_128_2", "_-_", "_01_09506000134352_10_ABC", "a_b_c" }),
            ("tabs", "data\tfilename\none\tn1\ntwo\t", new (int, string, string)[] { (2, "one", "n1"), (3, "two", null) }, new[] { "n1", "two" }),
            ("quotes", "data,fileName\n\"a,b\",q\n\"x \"\"y\"\"\",z", new (int, string, string)[] { (2, "a,b", "q"), (3, "x \"y\"", "z") }, new[] { "q", "z" }),
            ("empty", "", new (int, string, string)[] {  }, new string[0]),
            ("a capital I with a dot in a header", "f\u0130le,data\n1,a", new (int, string, string)[] { (2, "a", null) }, new[] { "a" }),
            ("a dotless i in a header", "F\u0131LE,DATA\n1,a", new (int, string, string)[] { (2, "a", null) }, new[] { "a" }),
        };

        static readonly (byte[] Bytes, string Text)[] WebsiteCsvDecoding =
        {
            (new byte[] { 0x61, 0x2c, 0x62 }, "a,b"),
            (new byte[] { 0xef, 0xbb, 0xbf, 0x75, 0x72, 0x6c }, "url"),
            (new byte[] { 0x63, 0x61, 0x66, 0xc3, 0xa9 }, "caf\u00e9"),
            (new byte[] { 0x63, 0x61, 0x66, 0xe9 }, "caf\u00e9"),
            (new byte[] { 0x80, 0x81, 0x8d, 0x8f, 0x90, 0x9d, 0x9f, 0xa0, 0xff }, "\u20ac\u0081\u008d\u008f\u0090\u009d\u0178\u00a0\u00ff"),
            (new byte[] { 0xc3, 0x28 }, "\u00c3("),
            (new byte[] { 0xe6, 0x97, 0xa5, 0xe6, 0x9c, 0xac }, "\u65e5\u672c"),
            (new byte[] { 0xed, 0xa0, 0x80 }, "\u00ed\u00a0\u20ac"),
            (new byte[] {  }, ""),
        };
    }
}
