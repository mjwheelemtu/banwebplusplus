var count = 0;
var calList = [];
function addCourseToCalendar(crn, courseNum) {
	$.get('/public/getCourseInfoForCalendar/' + crn + "/" + courseNum, function(responseTxt){
		console.log(crn);
		console.log(responseTxt);
		CRNList.push(crn);
		console.log(calList);

		var data = responseTxt[0];
		var days = data.Days.split(""); //puts days list into an array
		var timeTxt = data.SectionTime;
		var time = data.SectionTime.split("-"); //splits the time into an array with start and end time
		var startTime = time[0].split(" ")[0];
		var startTimeAP = time[0].split(" ")[1];
		var startTimeH = parseInt(startTime.split(":")[0], 10);
		var startTimeM = parseInt(startTime.split(":")[1], 10);
		var roundedStartTime = roundTime(startTimeH, startTimeM);
			startTimeH = roundedStartTime.timeH;
			startTimeM = roundedStartTime.timeM;

		var year = data.Year;
		var date = data.Dates.split("\/");
		var startMonth = date[0];
		var startDate = date[1].split("-")[0];
		var endDate = startDate[1];
		var endMonth = date[2];
		var course = data.CourseNum;
		
		var endTime = time[1].split(" ")[0];
		var endTimeAP = time[1].split(" ")[1];
		var endTimeH = endTime.split(":")[0];
		var endTimeM = endTime.split(":")[1];
		var roundedEndTime = roundTime(endTimeH, endTimeM);
			endTimeH = roundedEndTime.timeH;
			endTimeM = roundedEndTime.timeM;

		console.log(startTimeH+":"+startTimeM + " - " + endTimeH+":"+endTimeM);

		//THIS IS TESTING FOR GENERATING THE ICS FILE!
		var str = "BEGIN:VCALENDAR\nVERSION:2.0\nPRODID:-//hacksw/handcal//NONSGML v1.0//EN\n";
		//for(var i = 0; i < CRNList.length; i++) {
			var actualStartH = 0;
			if(startTimeAP == "pm") {
				actualStartH = parseInt(startTimeH + 12);
			}
			var actualEndH = 0;
			if(endTimeAP == "pm") {
				actualEndH = parseInt(endTimeH + 12);
			}
			str = str + "BEGIN:VEVENT\n";
			str = str + "UID:" + "uid1@example.com\n"; //NEED TO GRAB EMAIL
			str = str + "DTSTAMP:" + year + startMonth + startDate + "T" + actualStartH + startTimeM + "00Z\n";
			str = str + "ORGANIZER;CN=John Doe:MAILTO:john.doe@example.com\n" //need to grab name and email here too
			str = str + "DTSTART:" + year + startMonth + startDate + "T" + actualStartH + startTimeM + "00Z\n";
			str = str + "DTEND:" + year + endMonth + endMonth + "T" + actualEndH + endTimeM + "00Z\n";
			str = str + "SUMMARY:" + course + "\n";
			str = str + "END:VEVENT\n";
		//}
		str = str + "END:VCALENDAR";
		calList.push(str);
		console.log(calList);
		console.log(str);


		$.each(days, function(i, day){
			if(startTimeM == 0){
				$("." + day + "-" + startTimeH + startTimeAP).addClass('full');
				$("." + day + "-" + startTimeH + startTimeAP).html(courseNum + "<br/>" + timeTxt);
				$("." + day + "-" + startTimeH + startTimeAP).attr('data-target', '.courseInfoBox');
				$("." + day + "-" + startTimeH + startTimeAP).attr('data-toggle', 'modal');
				$("." + day + "-" + startTimeH + startTimeAP).attr('data-coursenum', courseNum);
			}else{
				$("." + day + "-" + startTimeH + startTimeAP).addClass('bottomHalf');
				var newStartTimeH = (startTimeH == 12 ? 1 : startTimeH + 1);
				$("." + day + "-" + newStartTimeH + startTimeAP).addClass('full');
				$("." + day + "-" + startTimeH + startTimeAP).html(courseNum + "<br/>" + timeTxt);
				$("." + day + "-" + startTimeH + startTimeAP).attr('data-target', '.courseInfoBox');
				$("." + day + "-" + startTimeH + startTimeAP).attr('data-toggle', 'modal');
				$("." + day + "-" + startTimeH + startTimeAP).attr('data-coursenum', courseNum);
			}
		});
	});
}

function roundTime(timeH, timeM) {
	//round start time to half hour
	if(timeM >= 15 && timeM <= 45){
		timeM = 30;

	//round to this hour
	} else if(timeM < 15) {
		timeM = 0;

	//round to next hour
	} else if(timeM > 45) {
		timeM = 0;
		timeH++;
	}

	return {'timeH': timeH, 'timeM': timeM};
}