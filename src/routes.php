<?php

use Slim\Http\Request;
use Slim\Http\Response;

// Routes

//Add Username to DB (test)
$app->get('/newUser/{username}', function (Request $request, Response $response, $args) {
	$username = $args['username'];
	$mapper = new UserMapper($this->db);
	$users = $mapper->addUser($username);
});

$app->get('/getAvailableSemesters', function(Request $request, Response $response) {
	$courseMapper = new CourseMapper($this->db);
	$semesterList = $courseMapper->getAvailableSemesters();
	$response = $response->withJson($semesterList);
	return $response;
});

/* Search for course by name (Intro to Programming) or title (CS 1121)
	returns JSON object */
$app->get('/search/{semester}/{query}', function(Request $request, Response $response, $args) {
	$query = $args['query'];
	$semester = $args['semester'];
	$courseMapper = new CourseMapper($this->db);
	$results = $courseMapper->search($query, $semester);
	$response = $response->withJson($results);
	return $response;
});

$app->get('/search/{semester}/', function(Request $request, Response $response, $args) {
	$results = "Please enter a search query.";
	$response = $response->withJson($results);
	return $response;
});

$app->get('/getCourseInfo/{semester}/{courseNum}', function(Request $request, Response $response, $args) {
	$query = $args['courseNum'];
	$semester = $args['semester'];
	$courseMapper = new CourseMapper($this->db);
	$results = $courseMapper->getCourseInfo($query, $semester);
	$response = $response->withJson($results);
	return $response;
});

$app->get('/getCourseInfoForCalendar/{crn}/{courseNum}', function(Request $request, Response $response, $args) {
	$crn = $args['crn'];
	$courseNum = $args['courseNum'];
	$courseMapper = new CourseMapper($this->db);
	$results = $courseMapper->getCourseInfoForCalendar($crn, $courseNum);
	$response = $response->withJson($results);
	return $response;
});

$app->get('/search/', function(Request $request, Response $response, $args) {
	echo "Please enter search query";
});

$app->post('/verifyIdToken', function(Request $request, Response $response) {
	$idToken = $request->getParsedBody()['idtoken'];
	$email = $request->getParsedBody()['email'];
	$name = $request->getParsedBody()['name'];
	$userMapper = new UserMapper($this->db);
	$result = $userMapper->verifyIdToken($idToken, $email, $name);
	return $result;
});

$app->get('/addSchedule', function(Request $request, Response $response)
{
	// TODO: What do I add here?
	$scheduleMapper = new ScheduleMapper($this->db);
	$scheduleMapper->AddSchedule($ScheduleName, $UserID, $Semester, $Year);
});
		  

$app->get('/addCourseToCalendar/{crn}', function(Request $request, Response $response) {
	//TODO:What do I do here?
	$scheduleMapper = new ScheduleMapper($this->db);
	$scheduleMapper->AddCourseToSchedule($ScheduleName, $UserID, $CRN);
});
