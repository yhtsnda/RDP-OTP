﻿using System;
using System.Diagnostics;
using System.DirectoryServices;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace de.sbeh.rdp_otp
{
    class Program
    {
        static int Main(string[] args)
        {
            var newpass = @"";

            {
                var oldpass = File.ReadAllText(@"oldPass.txt");

                {
                    var passchars = @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                    var rand = new Random();
                    for (var i = 0; i < 8; ++i)
                        newpass += passchars[rand.Next(passchars.Length)];
                }

                var e = new DirectoryEntry(string.Format(@"WinNT://{0}/{1},User", Environment.UserDomainName, Environment.UserName));
                e.Invoke(@"ChangePassword", oldpass, newpass);

                File.WriteAllText(@"oldPass.txt", newpass);
            }

            WebRequest request;

            {
                string encrypted;

                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(1024))
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.LoadPublicKeyPEM(File.ReadAllText(@"pubkey.pem", Encoding.UTF8));

                    encrypted = Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(newpass), false));
                }

                request = WebRequest.Create(@"http://zxing.org/w/chart?cht=qr&chs=350x350&chld=L&choe=UTF-8&chl=" +
                    Uri.EscapeDataString(encrypted));
            }

            using (var qrcode = new MemoryStream())
            {
                using (var response = request.GetResponse())
                    response.GetResponseStream().CopyTo(qrcode);

                var uploadTo = File.ReadAllText(@"uploadTo.url", Encoding.UTF8);

                var match = new Regex(@"^\w://(?<Credentials>(?<User>[^:@/]+):(?<Pass>[^@/]+)@)").Match(uploadTo);
                if (match.Success)
                {
                    uploadTo = uploadTo.Remove(match.Groups[@"Credentials"].Index, match.Groups[@"Credentials"].Length);

                    request = WebRequest.Create(uploadTo);
                    request.Credentials = new NetworkCredential(match.Groups[@"User"].Value, match.Groups[@"Pass"].Value);
                }
                else
                    request = WebRequest.Create(uploadTo);

                try
                {
                    request.Timeout = 20000;
                }
                catch { }

                try
                {
                    ((FtpWebRequest)request).Method = WebRequestMethods.Ftp.UploadFile;
                    ((FtpWebRequest)request).UsePassive = true;
                }
                catch
                {
                    try
                    {
                        ((HttpWebRequest)request).Method = WebRequestMethods.Http.Put;
                    }
                    catch
                    {
                        try
                        {
                            ((FileWebRequest)request).Method = WebRequestMethods.File.UploadFile;
                        }
                        catch
                        {
                            throw new Exception(@"Unknown protocol in your URL, please check file: uploadTo.url");
                        }
                    }
                }

                try
                {
                    request.ContentLength = qrcode.Length;
                }
                catch { }

                try
                {
                    request.ContentType = @"image/png";
                }
                catch { }

                using (var stream = request.GetRequestStream())
                    qrcode.WriteTo(stream);

                request.GetResponse();
            }

            return 0;
        }
    }
}