<?php
/**
 * Iris Story Weaver - Sync Backend
 *
 * Drop this next to story_weaver.html on any PHP server.
 * Creates a _story_data/ folder to store state.
 * No database, no dependencies, no accounts.
 *
 * API:
 *   GET  ?action=pull          → returns { data, presence, feed, version }
 *   POST ?action=push          → body: { data, user, changeDesc } → saves data, returns { version }
 *   POST ?action=presence      → body: { user } → updates heartbeat
 *   POST ?action=feed          → body: { entry } → adds to activity feed
 */

header('Content-Type: application/json');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: GET, POST, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') { http_response_code(204); exit; }

// Storage directory (auto-created, add to .gitignore)
$dir = __DIR__ . '/_story_data';
if (!is_dir($dir)) { mkdir($dir, 0755, true); }

$dataFile     = "$dir/data.json";
$presenceFile = "$dir/presence.json";
$feedFile     = "$dir/feed.json";
$versionFile  = "$dir/version.txt";

$action = $_GET['action'] ?? $_POST['action'] ?? 'pull';

// --- Helper: atomic file read/write with locking ---
function readJSON($path) {
    if (!file_exists($path)) return null;
    $fp = fopen($path, 'r');
    if (!$fp) return null;
    flock($fp, LOCK_SH);
    $raw = stream_get_contents($fp);
    flock($fp, LOCK_UN);
    fclose($fp);
    $decoded = json_decode($raw, true);
    return $decoded;
}

function writeJSON($path, $data) {
    $fp = fopen($path, 'c');
    if (!$fp) return false;
    flock($fp, LOCK_EX);
    ftruncate($fp, 0);
    fwrite($fp, json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE));
    fflush($fp);
    flock($fp, LOCK_UN);
    fclose($fp);
    return true;
}

function getVersion() {
    global $versionFile;
    if (!file_exists($versionFile)) return 0;
    return (int) file_get_contents($versionFile);
}

function bumpVersion() {
    global $versionFile;
    $v = getVersion() + 1;
    file_put_contents($versionFile, $v);
    return $v;
}

// --- Clean stale presence (older than 15 seconds) ---
function cleanPresence() {
    global $presenceFile;
    $presence = readJSON($presenceFile) ?? [];
    $now = time();
    $cleaned = [];
    foreach ($presence as $uid => $p) {
        if (($now - ($p['lastSeen'] ?? 0)) < 15) {
            $cleaned[$uid] = $p;
        }
    }
    writeJSON($presenceFile, $cleaned);
    return $cleaned;
}

// --- Handle actions ---
switch ($action) {

    case 'pull':
        // Return current data + presence + recent feed + version
        $sinceVersion = isset($_GET['since']) ? (int)$_GET['since'] : 0;
        $currentVersion = getVersion();

        $response = [
            'version' => $currentVersion,
            'presence' => cleanPresence(),
        ];

        // Only send full data if version changed (saves bandwidth)
        if ($sinceVersion < $currentVersion || $sinceVersion === 0) {
            $response['data'] = readJSON($dataFile);
        }

        // Last 20 feed entries
        $feed = readJSON($feedFile) ?? [];
        $response['feed'] = array_slice($feed, -20);

        echo json_encode($response);
        break;

    case 'push':
        // Save data from client
        $input = json_decode(file_get_contents('php://input'), true);
        if (!$input || !isset($input['data'])) {
            http_response_code(400);
            echo json_encode(['error' => 'Missing data']);
            exit;
        }

        writeJSON($dataFile, $input['data']);
        $newVersion = bumpVersion();

        // Update presence if user info included
        if (isset($input['user'])) {
            $presence = readJSON($presenceFile) ?? [];
            $uid = $input['user']['id'] ?? 'unknown';
            $presence[$uid] = [
                'name'     => $input['user']['name'] ?? 'Anonymous',
                'color'    => $input['user']['color'] ?? '#e94560',
                'tab'      => $input['user']['tab'] ?? '',
                'selection'=> $input['user']['selection'] ?? '',
                'lastSeen' => time(),
            ];
            writeJSON($presenceFile, $presence);
        }

        // Add to feed if change description provided
        if (!empty($input['changeDesc'])) {
            $feed = readJSON($feedFile) ?? [];
            $feed[] = [
                'name'   => $input['user']['name'] ?? 'Anonymous',
                'color'  => $input['user']['color'] ?? '#e94560',
                'action' => $input['changeDesc'],
                'ts'     => time() * 1000, // JS-compatible milliseconds
            ];
            // Keep last 100 entries
            if (count($feed) > 100) $feed = array_slice($feed, -100);
            writeJSON($feedFile, $feed);
        }

        echo json_encode(['version' => $newVersion, 'ok' => true]);
        break;

    case 'presence':
        // Heartbeat only — no data change
        $input = json_decode(file_get_contents('php://input'), true);
        if (!$input || !isset($input['user'])) {
            http_response_code(400);
            echo json_encode(['error' => 'Missing user']);
            exit;
        }

        $presence = readJSON($presenceFile) ?? [];
        $uid = $input['user']['id'] ?? 'unknown';
        $presence[$uid] = [
            'name'     => $input['user']['name'] ?? 'Anonymous',
            'color'    => $input['user']['color'] ?? '#e94560',
            'tab'      => $input['user']['tab'] ?? '',
            'selection'=> $input['user']['selection'] ?? '',
            'lastSeen' => time(),
        ];
        writeJSON($presenceFile, $presence);

        echo json_encode(['ok' => true, 'presence' => cleanPresence()]);
        break;

    case 'feed':
        // Add a feed entry without pushing data
        $input = json_decode(file_get_contents('php://input'), true);
        $feed = readJSON($feedFile) ?? [];
        if (isset($input['entry'])) {
            $feed[] = $input['entry'];
            if (count($feed) > 100) $feed = array_slice($feed, -100);
            writeJSON($feedFile, $feed);
        }
        echo json_encode(['ok' => true]);
        break;

    default:
        http_response_code(400);
        echo json_encode(['error' => 'Unknown action: ' . $action]);
}
