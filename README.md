# Html2Article

.NET平台下，一个高效的从Html中提取正文的工具。  
正文提取采用了基于文本密度的提取算法，支持从压缩的Html文档中提取正文，每个页面平均提取时间为30ms，正确率在95%以上。  
![Html2Article](http://stanzhai.github.io/images/project/Html2Article.png)

## Html2Article特色

* 标签无关，提取正文不依赖标签；
* 支持从压缩的html文档中提取正文内容；
* 支持带标签输出原始正文；
* 核心算法简洁高效，平均提取时间在30ms左右。

## 让你的项目支持Html正文提取

- **`PM> Install-Package Html2Article`**
- **引入命名空间`using StanSoft;`。**
- **添加如下代码：**

```C#
// html为你要提取的html文本
string html = "<html>....</html>";
// article对象包含Title(标题)，PublishDate(发布日期)，Content(正文)和ContentWithTags(带标签正文)四个属性
Article article = Html2Article.GetArticle(html);
```

### 离线分块入库（AddChunk / Finish）

网络下载得到的Html可能按块到达，块边界会切断标签或实体。每个 `Html2Article` 实例维护独立的拼接缓冲与配置，跨块保留未闭合的标签和实体，`Finish` 时对完整Html只执行一次解析（标题、日期、纯文本、带标签正文来自同一次解析，实体只解码一次）：

```C#
var parser = new Html2Article("http://www.example.com/news/1.html"); // 传入来源Url可自动补全相对链接
while ((string chunk = ReadNextNetworkBlock()) != null)
{
    parser.AddChunk(chunk);
}
Article article = parser.Finish();
```

实例之间配置互不影响，可安全用于并发批次；`Finish` 可重复调用（返回同一次结果），之后再次 `AddChunk` 即开始新一轮解析。

## Html2Article类

- **Html2Article类是提取正文的核心类**
- **Html2Article配置说明**（实例属性，每个解析器独立持有）
	* AppendMode：是否使用正文追加模式，默认为false，设置为true会将更多符合条件的文本添加到正文。
	* Depth：分析的深度，默认为6，对于行空隙较大的页面可增加此值。
	* LimitCount：字符限定数，当分析的文本数量达到限定数则认为进入正文内容，默认为180个字符。
	* BaseUrl：文章来源地址，设置后带标签正文中的相对链接会被补全为绝对链接，不设置则不处理。
	* GetArticle(string html)：静态入口，从Html文本中获取Article（内部使用默认配置的新实例）。
	* GetArticle(string html, string baseUrl)：静态入口，同时按baseUrl补全相对链接。
	* AddChunk(string chunk) / Finish()：分块流式入库，适用于离线文章按网络块拼接的场景。

## License

[Apache 2.0](http://www.apache.org/licenses/LICENSE-2.0)
