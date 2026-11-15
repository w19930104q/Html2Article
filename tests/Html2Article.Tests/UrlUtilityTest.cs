using StanSoft;
using NUnit.Framework;

namespace StanSoft.Tests
{
    [TestFixture]
    public class UrlUtilityTest
    {
        [Test]
        public void FixUrl()
        {
            string baseUrl = "http://www.stan.com";
            string html = "<a href='http://www.a.com/a.html'></a>\r\n<img src='new.jpg' />info";
            string result = UrlUtility.FixUrl(baseUrl, html);

            Assert.AreEqual("<a href='http://www.a.com/a.html'></a>\r\n<img src=\"http://www.stan.com/new.jpg\" />info",
                result);
        }

        [Test]
        public void FixUrl_ResolvesRelativePathsAndFragments()
        {
            const string baseUrl = "https://www.stan.com/news/list.html";

            string html = "<a href=\"detail/1.html\">a</a>" +
                          "<img src='/images/p.png' />" +
                          "<a href=\"?page=2\">p</a>";

            string result = UrlUtility.FixUrl(baseUrl, html);

            StringAssert.Contains("href=\"https://www.stan.com/news/detail/1.html\"", result);
            StringAssert.Contains("src=\"https://www.stan.com/images/p.png\"", result);
            StringAssert.Contains("href=\"https://www.stan.com/news/list.html?page=2\"", result);
        }
    }
}
