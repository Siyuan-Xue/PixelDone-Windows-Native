[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = if ($PSScriptRoot) {
    Split-Path -Parent $PSScriptRoot
} else {
    (Get-Location).Path
}
$locales = @('en-US', 'zh-Hans', 'ar', 'fr', 'ru', 'es')
$catalog = @(
    @('PlatformSubtitle.Text', 'WINDOWS · NATIVE', 'WINDOWS · 原生', 'ويندوز · أصلي', 'WINDOWS · NATIF', 'WINDOWS · НАТИВНО', 'WINDOWS · NATIVO'),
    @('NewChecklist.PlaceholderText', 'NEW LIST', '新建清单', 'قائمة جديدة', 'NOUVELLE LISTE', 'НОВЫЙ СПИСОК', 'NUEVA LISTA'),
    @('SelectedChecklist.Header', 'SELECTED LIST', '当前清单', 'القائمة المحددة', 'LISTE SÉLECTIONNÉE', 'ВЫБРАННЫЙ СПИСОК', 'LISTA SELECCIONADA'),
    @('RenameChecklist.Content', 'RENAME', '重命名', 'إعادة تسمية', 'RENOMMER', 'ПЕРЕИМЕНОВАТЬ', 'RENOMBRAR'),
    @('DeleteChecklist.Content', 'DELETE', '删除', 'حذف', 'SUPPRIMER', 'УДАЛИТЬ', 'ELIMINAR'),
    @('RestoreTask.Content', 'RESTORE', '恢复', 'استعادة', 'RESTAURER', 'ВОССТАНОВИТЬ', 'RESTAURAR'),
    @('EmptyTrash.Content', 'EMPTY TRASH', '清空回收站', 'إفراغ سلة المهملات', 'VIDER LA CORBEILLE', 'ОЧИСТИТЬ КОРЗИНУ', 'VACIAR PAPELERA'),
    @('NewTodo.PlaceholderText', 'WHAT NEEDS TO BE DONE?', '需要完成什么？', 'ما الذي يجب إنجازه؟', 'QUE FAUT-IL FAIRE ?', 'ЧТО НУЖНО СДЕЛАТЬ?', '¿QUÉ HAY QUE HACER?'),
    @('AddTodo.Content', '+ ADD', '+ 添加', '+ إضافة', '+ AJOUTER', '+ ДОБАВИТЬ', '+ AÑADIR'),
    @('CancelBatch.Content', 'CANCEL', '取消', 'إلغاء', 'ANNULER', 'ОТМЕНА', 'CANCELAR'),
    @('MoveBatchToTrash.Content', 'MOVE TO TRASH', '移至回收站', 'نقل إلى سلة المهملات', 'METTRE À LA CORBEILLE', 'В КОРЗИНУ', 'MOVER A PAPELERA'),
    @('TaskInspector.Text', 'TASK INSPECTOR', '任务检查器', 'محرر المهمة', 'INSPECTEUR DE TÂCHE', 'РЕДАКТОР ЗАДАЧИ', 'INSPECTOR DE TAREA'),
    @('TaskTitle.Header', 'TITLE', '标题', 'العنوان', 'TITRE', 'НАЗВАНИЕ', 'TÍTULO'),
    @('TaskPriority.Header', 'PRIORITY', '优先级', 'الأولوية', 'PRIORITÉ', 'ПРИОРИТЕТ', 'PRIORIDAD'),
    @('TaskDueDate.Header', 'DUE DATE', '截止日期', 'تاريخ الاستحقاق', 'DATE D''ÉCHÉANCE', 'СРОК — ДАТА', 'FECHA LÍMITE'),
    @('TaskDueTime.Header', 'DUE TIME', '截止时间', 'وقت الاستحقاق', 'HEURE D''ÉCHÉANCE', 'СРОК — ВРЕМЯ', 'HORA LÍMITE'),
    @('TaskRepeat.Header', 'REPEAT', '重复', 'التكرار', 'RÉPÉTITION', 'ПОВТОР', 'REPETIR'),
    @('MoveTarget.Header', 'MOVE TO', '移动到', 'نقل إلى', 'DÉPLACER VERS', 'ПЕРЕМЕСТИТЬ В', 'MOVER A'),
    @('MoveTask.Content', 'MOVE', '移动', 'نقل', 'DÉPLACER', 'ПЕРЕМЕСТИТЬ', 'MOVER'),
    @('AttachImage.Content', 'ATTACH IMAGE', '添加图片', 'إرفاق صورة', 'JOINDRE UNE IMAGE', 'ПРИКРЕПИТЬ ИЗОБРАЖЕНИЕ', 'ADJUNTAR IMAGEN'),
    @('OpenImage.Content', 'OPEN IMAGE', '打开图片', 'فتح الصورة', 'OUVRIR L''IMAGE', 'ОТКРЫТЬ ИЗОБРАЖЕНИЕ', 'ABRIR IMAGEN'),
    @('RemoveImage.Content', 'REMOVE IMAGE', '移除图片', 'إزالة الصورة', 'RETIRER L''IMAGE', 'УДАЛИТЬ ИЗОБРАЖЕНИЕ', 'QUITAR IMAGEN'),
    @('SaveChanges.Content', 'SAVE CHANGES', '保存更改', 'حفظ التغييرات', 'ENREGISTRER', 'СОХРАНИТЬ', 'GUARDAR CAMBIOS'),
    @('AppearanceBehavior.Text', 'APPEARANCE & BEHAVIOR', '外观与行为', 'المظهر والسلوك', 'APPARENCE ET COMPORTEMENT', 'ВИД И ПОВЕДЕНИЕ', 'APARIENCIA Y COMPORTAMIENTO'),
    @('Theme.Header', 'THEME', '主题', 'السمة', 'THÈME', 'ТЕМА', 'TEMA'),
    @('Language.Header', 'LANGUAGE', '语言', 'اللغة', 'LANGUE', 'ЯЗЫК', 'IDIOMA'),
    @('ShowDdl.Header', 'SHOW DDL COUNTDOWN', '显示截止倒计时', 'إظهار العد التنازلي', 'AFFICHER LE COMPTE À REBOURS', 'ПОКАЗЫВАТЬ ОБРАТНЫЙ ОТСЧЁТ', 'MOSTRAR CUENTA REGRESIVA'),
    @('QuickDelete.Header', 'QUICK DELETE', '快速删除', 'الحذف السريع', 'SUPPRESSION RAPIDE', 'БЫСТРОЕ УДАЛЕНИЕ', 'ELIMINACIÓN RÁPIDA'),
    @('UpdatePrompts.Header', 'UPDATE PROMPTS', '更新提示', 'تنبيهات التحديث', 'NOTIFICATIONS DE MISE À JOUR', 'УВЕДОМЛЕНИЯ ОБ ОБНОВЛЕНИЯХ', 'AVISOS DE ACTUALIZACIÓN'),
    @('EnhancedAlarm.Header', 'ENHANCED X-HIGH ALARM', '增强型特高提醒', 'تنبيه X-HIGH المحسّن', 'ALARME X-HIGH RENFORCÉE', 'УСИЛЕННОЕ ОПОВЕЩЕНИЕ X-HIGH', 'ALARMA X-HIGH MEJORADA'),
    @('WorkspaceDock.Text', 'WORKSPACE DOCK', '工作区操作栏', 'شريط مساحة العمل', 'BARRE DE L''ESPACE DE TRAVAIL', 'ПАНЕЛЬ РАБОЧЕЙ ОБЛАСТИ', 'BARRA DEL ESPACIO DE TRABAJO'),
    @('AddPlacement.Header', 'ADD BUTTON PLACEMENT', '添加按钮位置', 'موضع زر الإضافة', 'POSITION DU BOUTON AJOUTER', 'ПОЛОЖЕНИЕ КНОПКИ ДОБАВЛЕНИЯ', 'POSICIÓN DEL BOTÓN AÑADIR'),
    @('DockLimitHelp.Text', 'CHOOSE UP TO FOUR ACTIONS. CHOOSING A FIFTH REMOVES THE OLDEST.', '最多选择四个操作。选择第五个时会移除最早选择的操作。', 'اختر حتى أربعة إجراءات. اختيار إجراء خامس يزيل الأقدم.', 'CHOISISSEZ JUSQU''À QUATRE ACTIONS. LA CINQUIÈME REMPLACE LA PLUS ANCIENNE.', 'ВЫБЕРИТЕ ДО ЧЕТЫРЁХ ДЕЙСТВИЙ. ПЯТОЕ ЗАМЕНИТ САМОЕ СТАРОЕ.', 'ELIGE HASTA CUATRO ACCIONES. LA QUINTA SUSTITUYE A LA MÁS ANTIGUA.'),
    @('Updates.Text', 'UPDATES', '更新', 'التحديثات', 'MISES À JOUR', 'ОБНОВЛЕНИЯ', 'ACTUALIZACIONES'),
    @('CheckUpdates.Content', 'CHECK FOR UPDATES', '检查更新', 'التحقق من التحديثات', 'RECHERCHER DES MISES À JOUR', 'ПРОВЕРИТЬ ОБНОВЛЕНИЯ', 'BUSCAR ACTUALIZACIONES'),
    @('OpenRelease.Content', 'OPEN RELEASE', '打开发布页', 'فتح صفحة الإصدار', 'OUVRIR LA VERSION', 'ОТКРЫТЬ РЕЛИЗ', 'ABRIR VERSIÓN'),
    @('Cloud.Text', 'CLOUD', '云端', 'السحابة', 'CLOUD', 'ОБЛАКО', 'NUBE'),
    @('Email.Header', 'EMAIL', '电子邮箱', 'البريد الإلكتروني', 'E-MAIL', 'ЭЛЕКТРОННАЯ ПОЧТА', 'CORREO ELECTRÓNICO'),
    @('Password.Header', 'PASSWORD', '密码', 'كلمة المرور', 'MOT DE PASSE', 'ПАРОЛЬ', 'CONTRASEÑA'),
    @('SignInRestore.Content', 'SIGN IN & RESTORE', '登录并恢复', 'تسجيل الدخول والاستعادة', 'SE CONNECTER ET RESTAURER', 'ВОЙТИ И ВОССТАНОВИТЬ', 'INICIAR SESIÓN Y RESTAURAR'),
    @('CreateAccount.Content', 'CREATE ACCOUNT', '创建账户', 'إنشاء حساب', 'CRÉER UN COMPTE', 'СОЗДАТЬ АККАУНТ', 'CREAR CUENTA'),
    @('FirstSignInHelp.Text', 'FIRST SIGN-IN REPLACES THIS NEW LOCAL WORKSPACE WITH CLOUD DATA.', '首次登录会用云端数据替换这个全新的本地工作区。', 'يستبدل تسجيل الدخول الأول مساحة العمل المحلية الجديدة ببيانات السحابة.', 'LA PREMIÈRE CONNEXION REMPLACE CE NOUVEL ESPACE LOCAL PAR LES DONNÉES DU CLOUD.', 'ПРИ ПЕРВОМ ВХОДЕ НОВАЯ ЛОКАЛЬНАЯ ОБЛАСТЬ БУДЕТ ЗАМЕНЕНА ДАННЫМИ ИЗ ОБЛАКА.', 'EL PRIMER INICIO DE SESIÓN SUSTITUYE ESTE ESPACIO LOCAL NUEVO POR LOS DATOS DE LA NUBE.'),
    @('SyncNow.Content', 'SYNC NOW', '立即同步', 'المزامنة الآن', 'SYNCHRONISER', 'СИНХРОНИЗИРОВАТЬ', 'SINCRONIZAR AHORA'),
    @('SignOut.Content', 'SIGN OUT', '退出登录', 'تسجيل الخروج', 'SE DÉCONNECTER', 'ВЫЙТИ', 'CERRAR SESIÓN'),
    @('ChangePasswordExpander.Header', 'CHANGE PASSWORD', '更改密码', 'تغيير كلمة المرور', 'CHANGER LE MOT DE PASSE', 'ИЗМЕНИТЬ ПАРОЛЬ', 'CAMBIAR CONTRASEÑA'),
    @('CurrentPassword.Header', 'CURRENT PASSWORD', '当前密码', 'كلمة المرور الحالية', 'MOT DE PASSE ACTUEL', 'ТЕКУЩИЙ ПАРОЛЬ', 'CONTRASEÑA ACTUAL'),
    @('NewPassword.Header', 'NEW PASSWORD', '新密码', 'كلمة المرور الجديدة', 'NOUVEAU MOT DE PASSE', 'НОВЫЙ ПАРОЛЬ', 'NUEVA CONTRASEÑA'),
    @('ConfirmPassword.Header', 'CONFIRM NEW PASSWORD', '确认新密码', 'تأكيد كلمة المرور الجديدة', 'CONFIRMER LE MOT DE PASSE', 'ПОДТВЕРДИТЕ НОВЫЙ ПАРОЛЬ', 'CONFIRMAR NUEVA CONTRASEÑA'),
    @('ChangePassword.Content', 'CHANGE PASSWORD', '更改密码', 'تغيير كلمة المرور', 'CHANGER LE MOT DE PASSE', 'ИЗМЕНИТЬ ПАРОЛЬ', 'CAMBIAR CONTRASEÑA'),
    @('SyncConflicts.Text', 'SYNC CONFLICTS', '同步冲突', 'تعارضات المزامنة', 'CONFLITS DE SYNCHRONISATION', 'КОНФЛИКТЫ СИНХРОНИЗАЦИИ', 'CONFLICTOS DE SINCRONIZACIÓN'),
    @('KeepLocal.Content', 'KEEP LOCAL', '保留本地版本', 'الاحتفاظ بالمحلي', 'GARDER LA VERSION LOCALE', 'ОСТАВИТЬ ЛОКАЛЬНУЮ', 'CONSERVAR LOCAL'),
    @('KeepCloud.Content', 'KEEP CLOUD', '保留云端版本', 'الاحتفاظ بالسحابة', 'GARDER LA VERSION CLOUD', 'ОСТАВИТЬ ОБЛАЧНУЮ', 'CONSERVAR NUBE')
)

for ($localeIndex = 0; $localeIndex -lt $locales.Count; $localeIndex++) {
    $locale = $locales[$localeIndex]
    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$builder.AppendLine('<root>')
    [void]$builder.AppendLine('  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>')
    [void]$builder.AppendLine('  <resheader name="version"><value>2.0</value></resheader>')
    [void]$builder.AppendLine('  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms</value></resheader>')
    [void]$builder.AppendLine('  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms</value></resheader>')
    foreach ($entry in $catalog) {
        $key = [System.Security.SecurityElement]::Escape($entry[0])
        $value = [System.Security.SecurityElement]::Escape($entry[$localeIndex + 1])
        [void]$builder.AppendLine("  <data name=`"$key`" xml:space=`"preserve`"><value>$value</value></data>")
    }
    [void]$builder.AppendLine('</root>')
    $targetDirectory = Join-Path $projectRoot "src\PixelDone.Windows\Strings\$locale"
    [System.IO.Directory]::CreateDirectory($targetDirectory) | Out-Null
    $targetPath = Join-Path $targetDirectory 'Resources.resw'
    [System.IO.File]::WriteAllText(
        $targetPath,
        $builder.ToString(),
        [System.Text.UTF8Encoding]::new($false))
}
