# بحث تقنيات صفحات سطح المكتب والأنميشن

تاريخ المراجعة: 18 أكتوبر 2026. هذه مقارنة واسعة للفئات والبرامج ذات الصلة؛ ليست ادعاءً بحصر كل برنامج موجود. راجعت الوثائق والمصادر الموضحة، ولم أشغّل هذه البرامج على جهاز Windows في بيئة الإنشاء. لا أنسب آلية داخلية لبرنامج مغلق لمجرد أنه يعرض التأثير المطلوب.

## البرامج والمشاريع

| البرنامج / المشروع | ما تثبته الوثائق أو الكود | علاقته بالمطلوب | المصدر الأولي |
|---|---|---|---|
| Fences | صفحات أيقونات مع سحب وتحريك، ومجموعات وFolder Portals | أقرب منتج جاهز للسلوك المطلوب؛ مدفوع، والتنفيذ الداخلي مغلق. عبارة touch-enabled في دليل الصفحات لا تثبت توافق لوحة لمس بعينها | [Pages](https://support.stardock.com/space/SHC/2814444135)، [المنتج](https://www.stardock.com/products/fences/) |
| DesktopUp | ربط كل صفحة بمجلد، وحفظ الترتيب، واختصارات وتأثير انتقال | يغيّر المحتوى المعروض بحسب المجلد؛ الدليل لا يكشف واجهة التنفيذ ولا يثبت سحب Precision Touchpad | [الدليل](https://www.gdzsoft.com/du/guide/) |
| WorkTop | مجلد مستقل لكل سطح، وإمكانية ربطه بسطح ويندوز الافتراضي | تبديل موقع مجلد Desktop جزء من النموذج؛ يختلف عن تصفية ملفات موجودة في المجلد نفسه | [المجلدات](https://worktop.dev/docs/desktop-folders/)، [الربط](https://worktop.dev/guides/different-icons-each-virtual-desktop/) |
| Dexpot | تخصيص الأيقونات وخيار مجلد منفصل لكل سطح | مرجع قديم للتبديل بين المجلدات؛ لا دليل هنا على سلاسة أو توافق Windows 25H2 | [شرح الخيارات](https://dexpot.de/forum/viewtopic.php?t=2491) |
| VirtuaWin + VWDesktopIcons | تحديد الأيقونات الظاهرة وترتيبها لكل سطح؛ يتطلب إغلاق Auto arrange وAlign to grid | إضافة قديمة مفيدة للمقارنة؛ وثائقها تعود إلى 2011، وليست حلاً مثبتاً للجهاز الحالي | [وثائق الإضافة](https://sourceforge.net/projects/virtuawin/files/Modules/VWDesktopIcons/) |
| ViPad | لوحات وتبويبات وصفحات، وإخفاء الأيقونات الأصلية أسفلها | يستخدم عرضًا خاصًا به؛ يختلف عن التحكم بأيقونات Explorer الأصلية | [الموقع والكود](https://lee-soft.com/vipad/) |
| SideSlide | مساحة اختصارات وحاويات قابلة للطي والإرساء | منظّم ومشغّل مستقل غني بالميزات؛ ليس محرك صفحات للعرض الأصلي | [الموقع](https://www.northglide.com/sideslide.html) |
| Nimi Places | حاويات تعرض محتوى مجلدات مختارة | فكرة folder containers، لا دليل في الصفحة على صفحات Explorer الأصلية | [الموقع](https://mynimi.net/Projects/Nimi-Places/) |
| Portals | عرض مجلدات داخل نوافذ مثبتة على سطح المكتب | عرض محتوى المجلدات بدلاً من تصفية العرض الأصلي | [الموقع](https://portals-app.com/) |
| Desktop Frames+ | إطارات وبوابات مجلدات وتخصيصات | بديل مفتوح المصدر لتنظيم المحتوى؛ راجعت وصفه وشجرة ملفاته، لا أنسب له محرك تحريك بعينه | [المستودع](https://github.com/limbo666/DesktopFramesPlus) |
| Pickets | الكود يخفي الأيقونات بنقل إحداثياتها إلى خارج الشاشة مع الاحتفاظ بمرجعها | يحافظ على مسار الملف، لكن الكود نفسه يعالج رجوع الأيقونات للشاشة بفعل Explorer؛ هذا يفسر هشاشة إحداثيات الإخفاء | [DesktopIconHider.cs](https://github.com/Creeptones/Pickets/blob/38052a30dd2cf9df6776ca03669803df340e1492/DesktopIconHider.cs) |
| DexCorral | الكود يدمج التطبيق وhook داخل عملية Explorer، ويعترض رسائل ListView | تكامل أصلي أعمق يمكنه التحكم بالرسم والتفاعل؛ يحتاج نشر Shell extension واختبارات توافق؛ لم أنسخ حقنه إلى DeskPages | [الكود](https://github.com/guHe330/DexCorral)، docs/WIN32_API_AND_HOOKS.md وCorralHook.cpp |
| iTop Easy Desktop | تجميع الأيقونات في صناديق وعرض خلفيات وميزات إضافية | منتج تنظيم شامل؛ وصف المنتج لا يكشف تقنية رسمه الداخلية | [الموقع](https://www.itopvpn.com/itop-easy-desktop) |
| Rainmeter | skins وويدجت لعرض البيانات والتفاعل على سطح المكتب | يصلح لبناء واجهة خاصة، ولا يحوّل بمفرده أيقونات Explorer إلى صفحات | [الموقع](https://www.rainmeter.net/) |
| Winstep Nexus | Dock لاختصارات التطبيقات ومؤثرات | مسار launcher، لا يحقق شرط صفحات العرض الأصلي | [الموقع](https://www.winstep.net/nexus.asp) |
| Wallpaper Engine | خلفيات مشاهد وويب وفيديو وتكامل مع DWM | مفيد لفهم فصل الخلفية عن العرض؛ وثائقه ليست API لصفحات الأيقونات | [التوثيق](https://docs.wallpaperengine.io/en/)، [DWM](https://help.wallpaperengine.io/en/performance/dwm.html) |
| Lively | راجعت كود اكتشاف Progman/WorkerW، وتغييرات طبقات ويندوز الحديثة، وإعادة الاتصال بعد Explorer | طريقة ربط خلفيات سطح المكتب؛ لا توفر تصفية الأيقونات أو صفحاتها | [WinDesktopCore.cs](https://github.com/lively-community/lively/blob/25a0a5f4ed1988ff42117d3a17feb321062237fe/src/Lively/Lively/Core/WinDesktopCore.cs) |
| DeskScapes | خلفيات ومؤثرات سطح المكتب | بديل ضمن فئة الخلفيات، لا دليل في المنتج على API لصفحات الأيقونات | [الموقع](https://www.stardock.com/products/deskscapes/) |
| rusty-desktop-icons | راجعت محرك رسم نسخ الأيقونات بـD3D11 وDirect2D وDirectComposition؛ يترك الخلفية دون التقاط في مساره | مرجع تقني مباشر لفصل الحركة عن إعادة رسم Explorer. DeskPages يستخدم صور صفحات كاملة، ولا ينسخ رسم الأيقونات أو نصوصها من هذه المكتبة | [الكود](https://github.com/s0d3s/rusty-desktop-icons/tree/c498cab72ea746ee7f0e4051225cdc2ce40e566e) |
| Windhawk | منصة تعديلات داخل العمليات؛ راجعت مثال تبديل ظهور الأيقونات | خيار لتطوير تكامل داخل Explorer، وليس مكتبة أنميشن جاهزة لحالتنا | [مثال المصدر](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/zen-desktop-toggle-icons.wh.cpp) |

## الطرق التقنية ومفاضلتها

| الطريقة | ما تحققه | القيد الذي يهم DeskPages |
|---|---|---|
| إخفاء الملفات بـHidden مع حفظ العضوية | يبقي المسارات والملفات في أماكنها | يرسل تغيرات إلى Explorer؛ يجب إخفاء إعادة الرسم أثناء التبديل، وShow Hidden items يؤثر على النتيجة |
| نقل الأيقونات خارج مساحة الشاشة | يغيّر العرض دون نقل الملف | Explorer قد يقيد المواضع أو يعيدها؛ حصل هذا بالفعل في اختبارات الجهاز السابقة |
| ربط Desktop بمجلد آخر | صفحات محتوى منفصلة | SHSetKnownFolderPath يغير المسار الذي تعيده Windows للتطبيقات؛ ليس مجرد تبديل بصري |
| قوائم وويدجت ترسم أيقوناتها الخاصة | حرية كاملة في تحريك الصفحات | يخالف شرط الاستمرار على عرض Explorer الأصلي إذا صار واجهة دائمة |
| hook أو Shell extension داخل Explorer | تحكم عميق في الرسم والتفاعل | توافق وتحمّل أعطال أصعب، ولا يلزم حقن الكود للحصول على حركة مؤقتة |
| تحريك أيقونات Explorer الحقيقية في كل إطار | الأيقونات نفسها تتحرك | يربط معدل الحركة بطابور رسائل Explorer وإعادة رسمه |
| صور صفحات + WPF | تنفيذ بسيط | النسخة القديمة كانت تمزج PrintWindow ومزامنة WPF وعمل Explorer؛ مصدر محتمل للتقطيع والالتقاط غير الصحيح |
| DWM thumbnail | عرض حي لنافذة أخرى | لا يجمد الصفحة القديمة؛ المصدر نفسه يتغير أثناء تبديل الأيقونات |
| Windows Graphics Capture / DXGI Duplication | التقاط مرئي/GPU متقدم | يحتاج إدارة تدفق إطارات ومراقبة الشاشات/الأجهزة؛ خيار لاحق إذا فشل التقاط الشاشة المستبعد منه الغطاء |
| صور صفحات + DirectComposition | رفع صورتين مرة واحدة، وحركة مستقلة عبر DWM | لا يزال يلزم الحصول على صورتي الصفحتين بشكل صحيح؛ لذلك أضفت فحص استبعاد الغطاء على ويندوز |
| IFolderView2.SetRedraw | تجميع تحديثات العرض | ليست API للحركة أو التصفية، ويجب ضمان إعادة تفعيل الرسم عند التعطل؛ لا حاجة لتعطيل عرض Explorer في المحرك الجديد |

## القرار والتغيير المنفذ

نفذت الخيار الأخير الخاص بالعرض: محرك C++ أصلي مستقل عن WPF، مع D3D11 وDirect2D وDirectComposition. يحتفظ البرنامج بعرض Explorer الأصلي عندما ينتهي الانتقال. لا يعيد رسم أسماء الملفات أو نسخ أيقوناتها يدويًا.

1. تُلتقط الصفحة الحالية مرة واحدة، وتُرفع إلى surface.
2. يظهر غطاء غير منشِّط وغير topmost، ويُنتظر اكتمال commit والعرض.
3. يطبق محرك الصفحات الحالي تغيرات العضوية والترتيب خلف الغطاء.
4. يُلتقط سطح المكتب الجديد، مع استبعاد نافذة الغطاء نفسها عبر WDA_EXCLUDEFROMCAPTURE. لا يُخفى الغطاء من أجل الالتقاط.
5. تُرفع صورة الهدف مرة واحدة، وتُحرَّك الصورتان بمنحنيين متزامنين عبر DirectComposition.
6. بعد اكتمال زمن الحركة مع هامش إطارات ومزامنة DWM، يختفي الغطاء ويعود التفاعل مع Explorer.

يوجد إلغاء عند تغير الشاشة أو DPI أو جلسة النظام أو عملية Explorer، ومؤقت حراسة لغطاء عالق. لا يتطلب هذا المحرك تعطيل رسم Explorer، أو إعادة ترتيب الأيقونات في كل إطار، أو تغيير مسار Desktop، أو تثبيت Wallpaper Engine/Fences. تثبيت برنامج خلفيات لن يصلح تزامن كود DeskPages تلقائيًا.

الكود الجديد مستقل؛ لم تُضمَّن شيفرة Rust/GPL من البرامج المقارنة. استُخدمت كوداتها كمرجع معماري. أدوات البناء Cross-compiler حُمّلت وجهزت محليًا، والـDLL المرفقة مخصصة لـWindows x64 ولا تحتاج حزمة C++ إضافية من المستخدم.

## حدود الإثبات

نجح التجميع الأصلي لـWindows وفحص الصادرات والاعتماديات، وفحوص منحنيات الحركة ودورة الحالة واختبارات محرك الصفحات. هذا لا يساوي قياس FPS على شاشة المستخدم. Animation-Test.cmd يختبر على جهاز Windows خمس حركات وصحة التقاط لون معروف خلف الغطاء، دون تعديل ملفات سطح المكتب. يوقف الاختبار بنص واضح إذا فشل الاستبعاد أو لم ينته الغطاء.

لأن الغطاء مستبعد من التقاط الشاشة، قد تسجّل برامج تسجيل الشاشة الصفحة التي تحته بدلاً من الأنميشن الذي تراه بالعين. هذا أثر مقصود لطريقة الالتقاط الحالية، ويجب أخذه في الحسبان عند تقييم الفيديوهات المسجلة برمجيًا.

مراجع Windows: [DirectComposition animation](https://learn.microsoft.com/en-us/windows/win32/directcomp/animation)، [CreateTargetForHwnd](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositiondevice-createtargetforhwnd)، [WaitForCommitCompletion](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositiondevice-waitforcommitcompletion)، [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)، [SHSetKnownFolderPath](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shsetknownfolderpath).
